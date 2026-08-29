using System.IO;
using System.Text;
using System.Windows.Automation;

namespace CredUiSmoke;

internal static class Program
{
    private const string Usage = $"""
        CredUiSmoke - smoke-test harness for CredUIPromptForWindowsCredentials

        Commands:
          packages              List LSA authentication-package ids. No dialog, instant.
          enumerate             Open the dialog, dump the UI Automation tree, optionally click
                                "More choices" and list the provider tiles, then Cancel. Unattended.
          auto                  Open the dialog, type the user name and the password from
                                CREDUI_SMOKE_PASSWORD, submit, and report what came back.
          pin                   Open the dialog, go to "More choices", pick the PIN tile, type
                                CREDUI_SMOKE_PIN, submit, and report what came back.
          shot                  Open the dialog, save a PNG of it, and Cancel. With --more, also
                                a PNG of the "More choices" tiles. Unattended.
          cmdlet                Photograph the dialog the REAL cmdlet raises. Starts a PowerShell
                                that imports the module and calls Get-CredUICredential with
                                --args, drives it with --step, captures, cancels, and reports what
                                the cmdlet produced. This is the one that makes claims about what
                                a script would see. Unattended.
          submit                Open the dialog and wait for a HUMAN to submit it, then report.
                                Cancels itself at --timeout so it can never block forever.
          drive --label TEXT    Drive a dialog raised by ANOTHER process - the PowerShell session
                                running the real cmdlet - found by its message text, so the module
                                can be smoke-tested end to end. Types CREDUI_SMOKE_PASSWORD and
                                clicks OK, or --cancel.

        Options:
          --flags 0x12          dwFlags for the prompt. Default 0x12
                                (CREDUIWIN_AUTHPACKAGE_ONLY | CREDUIWIN_CHECKBOX), what the module uses.
                                Ignored by `cmdlet`, which lets the cmdlet choose its own.
          --in-package NAME|N   Seed pulAuthPackage on the way in. Name is looked up via LSA.
                                Default 0, which is what the module passes.
          --user NAME           Seed the user name through CredPackAuthenticationBuffer.
          --auth-error N        dwAuthError, e.g. 1326 for the "wrong password" banner.
          --timeout SECONDS     How long to wait before cancelling. Default 45 (300 for `submit`).
          --more                enumerate: also click "More choices" and dump the tiles.
          --type-probe          enumerate: type throwaway characters into the password box and
                                re-scan, because the peek glyph is only templated once the box
                                has something in it.
          --no-submit           pin: walk to the PIN field and stop. A wrong PIN counts against
                                Windows Hello's failure counter, so nothing is guessed.
          --dump                Dump the full UI Automation tree in commands that do not by default.
          --shot                Save a PNG of the dialog at each interesting moment. Implied by
                                `shot` and by `cmdlet`. Only the dialog's own rectangle is captured.
          --shot-screen         --shot, but capture the whole desktop instead of just the dialog.
                                Everything else on screen ends up in the file; use deliberately.
          --shot-dir PATH       Where the PNGs go. Default %TEMP%\CredUiSmoke\<timestamp>-<pid>.
          --label TEXT          Message text on the dialog; also how the harness finds its window.
                                For `cmdlet` it defaults to the cmdlet's own default message, so a
                                bare call is found without passing anything.
          --args "..."          cmdlet: parameters for Get-CredUICredential, passed through
                                verbatim, e.g. --args "-UserName johndoe -ShowSaveCheckbox".
                                Omit for a bare call.
          --module PATH         cmdlet: the module manifest to import. Defaults to the
                                CredUICredential.psd1 found above the harness.
          --step VERB[:ARG]     What to do to the dialog before, between and after captures.
                                Repeatable; the steps run in the order given.

        Steps (--step):
        {Steps.Help}

        Secrets are read from the environment and never printed. Only the length and the
        character-class histogram of a decoded password are reported, and a step that types is
        logged by length rather than by content. A screenshot is of the dialog only, and the
        dialog shows a typed password as dots - unless a `peek` step is asked for, which holds the
        reveal glyph down precisely so that the capture shows it in the clear.

        Exit codes: 0 ok, 1 bad usage, 2 dialog never appeared, 3 cancelled on timeout,
                    4 watchdog had to kill the process, 5 the prompt or a step failed.
        """;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 1 : 0;
        }

        var options = Options.Parse(args, out var error);
        if (options is null || error is not null)
        {
            Console.Error.WriteLine(error ?? "Could not make sense of the arguments.");
            return 1;
        }

        // `shot` and `cmdlet` exist to produce pictures, so neither needs asking twice.
        Screenshot.Enabled = options.Shot || options.Command is "shot" or "cmdlet";
        Screenshot.FullScreen = options.ShotScreen;
        Screenshot.DirectoryOverride = options.ShotDir;
        if (Screenshot.Enabled)
        {
            Console.WriteLine($"screenshots: {Screenshot.OutputDirectory}");
        }

        var packageMap = Diagnostics.LookupPackages(out var lsaFailure);
        if (lsaFailure is not null)
        {
            Console.WriteLine($"WARNING: {lsaFailure}");
        }

        // Nothing here may outlive its welcome: a modal dialog nobody can see would otherwise
        // hold the process open indefinitely.
        StartWatchdog(options.Timeout + TimeSpan.FromSeconds(60));
        WarnAboutStrandedDialogs();

        return options.Command switch
        {
            "packages" => Packages(packageMap),
            "enumerate" => Enumerate(options, packageMap),
            "auto" => Auto(options, packageMap),
            "pin" => Pin(options, packageMap),
            "shot" => Shot(options, packageMap),
            "cmdlet" => Cmdlet(options),
            "submit" => Submit(options, packageMap),
            "drive" => Drive(options),
            _ => Unknown(options.Command),
        };
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        Console.Error.WriteLine(Usage);
        return 1;
    }

    /// <summary>
    ///     A credential dialog belonging to somebody else is a hazard in both directions: this
    ///     harness could drive it, and it can steal the keystrokes meant for ours.
    /// </summary>
    private static void WarnAboutStrandedDialogs()
    {
        try
        {
            foreach (AutomationElement window in AutomationElement.RootElement.FindAll(
                         TreeScope.Children,
                         new PropertyCondition(AutomationElement.NameProperty, "Windows Security")))
            {
                if (window.Current.ProcessId != Environment.ProcessId)
                {
                    Console.WriteLine(
                        $"WARNING: a credential dialog from process {window.Current.ProcessId} is already on the " +
                        "desktop. Close it before trusting anything below.");
                }
            }
        }
        catch (ElementNotAvailableException)
        {
        }
    }

    private static void StartWatchdog(TimeSpan limit)
    {
        var thread = new Thread(() =>
        {
            Thread.Sleep(limit);
            Console.Error.WriteLine($"HARNESS-WATCHDOG: still alive after {limit.TotalSeconds:0}s, killing the process.");
            Console.Out.Flush();
            Environment.Exit(4);
        })
        { IsBackground = true, Name = "watchdog" };
        thread.Start();
    }

    private static int Packages(Dictionary<uint, List<string>> packageMap)
    {
        Console.WriteLine("LSA authentication packages known on this machine:");
        foreach (var pair in packageMap.OrderBy(p => p.Key))
        {
            Console.WriteLine($"  id={pair.Key,-5} {string.Join(" / ", pair.Value)}");
        }

        var missing = Diagnostics.KnownPackageNames
            .Where(name => !packageMap.Values.Any(list => list.Contains(name)))
            .ToArray();
        Console.WriteLine($"  not registered: {string.Join(", ", missing)}");
        Console.WriteLine();
        Console.WriteLine("NOTE: the module seeds pulAuthPackage with Kerberos' id, which is what keeps the");
        Console.WriteLine("      PIN and smart-card tiles off \"More choices\". It does not read the id back:");
        Console.WriteLine("      credui echoes the seed, so the value describes the request, not the");
        Console.WriteLine("      credential. The buffer's KERB_LOGON_SUBMIT_TYPE is what discriminates.");
        return 0;
    }

    /// <summary>
    ///     Opens the prompt, looks at it, and closes it. The whole point is that this needs nobody
    ///     at the keyboard.
    /// </summary>
    private static int Enumerate(Options options, Dictionary<uint, List<string>> packageMap)
    {
        var runner = NewRunner(options);
        Console.WriteLine(Header(options));
        runner.Start();

        var dialog = Ui.WaitForDialog(Environment.ProcessId, options.Label, TimeSpan.FromSeconds(25));
        if (dialog is null)
        {
            return NotFound(runner, packageMap);
        }

        Thread.Sleep(600);
        Focus(dialog);
        Screenshot.Capture(dialog, "initial");
        Console.WriteLine("--- initial tree ---");
        Console.WriteLine(Ui.DumpTree(dialog, includeRects: true));

        ReportPasswordSurface(dialog);

        if (options.TypeProbe)
        {
            // The XAML PasswordBox only templates its reveal button once there is something to
            // reveal, so an empty box says nothing about whether peek came back.
            var field = Ui.FindPasswordField(dialog);
            if (field is not null)
            {
                Focus(dialog);
                if (Ui.Focus(field))
                {
                    Ui.TypeText("probe");
                    Thread.Sleep(500);
                    Console.WriteLine("--- password surface after typing 5 throwaway characters ---");
                    Screenshot.Capture(dialog, "password-typed");
                    ReportPasswordSurface(dialog);
                    Console.WriteLine(Ui.DumpTree(dialog, includeRects: true));
                }
            }
        }

        if (options.More)
        {
            var more = Ui.FindByNameContains(dialog, "More choices", "More options");
            if (more is null)
            {
                Console.WriteLine("No \"More choices\" affordance is present.");
            }
            else
            {
                Console.WriteLine($"Clicking '{more.Current.Name}' ...");
                if (!Ui.Invoke(more))
                {
                    Ui.Focus(more);
                    Ui.SendVirtualKey(Native.VK_RETURN);
                }

                Thread.Sleep(900);
                Screenshot.Capture(dialog, "more-choices");
                Console.WriteLine("--- tree after More choices ---");
                Console.WriteLine(Ui.DumpTree(dialog, includeRects: true));
                Console.WriteLine("--- credential provider tiles ---");
                foreach (var tile in Tiles(dialog))
                {
                    Console.WriteLine($"  tile: '{tile.Current.Name}' ({Trim(tile.Current.ControlType.ProgrammaticName)})");
                }
            }
        }

        Console.WriteLine(Ui.Cancel(dialog));
        var closed = runner.Wait(TimeSpan.FromSeconds(10));
        Console.WriteLine(closed ? runner.DescribeOutcome(packageMap) : "The prompt did not return after Cancel.");
        runner.FreeOutputBuffer();
        return closed ? 0 : 3;
    }

    private static int Auto(Options options, Dictionary<uint, List<string>> packageMap)
    {
        var password = Environment.GetEnvironmentVariable("CREDUI_SMOKE_PASSWORD");
        if (string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine("Set CREDUI_SMOKE_PASSWORD first. It is never printed.");
            return 1;
        }

        var user = options.User ?? Environment.UserName;
        return DriveAndSubmit(
            options,
            packageMap,
            dialog =>
            {
                var passwordField = Ui.FindPasswordField(dialog);
                if (passwordField is null)
                {
                    Console.Error.WriteLine("No password field on the dialog.");
                    return false;
                }

                if (string.IsNullOrEmpty(options.Seed))
                {
                    var userField = Ui.FindUserNameField(dialog);
                    if (userField is not null && Ui.Focus(userField))
                    {
                        Ui.TypeText(user);
                        Console.WriteLine($"Typed user name ({user.Length} characters).");
                    }
                }

                if (!Ui.Focus(passwordField))
                {
                    Console.Error.WriteLine("Could not focus the password field.");
                    return false;
                }

                Ui.TypeText(password);
                Console.WriteLine($"Typed password ({password.Length} characters, not shown).");
                Thread.Sleep(300);
                Console.WriteLine("--- password surface with the password typed ---");
                Screenshot.Capture(dialog, "filled");
                ReportPasswordSurface(dialog);
                return ClickOk(dialog);
            });
    }

    private static int Pin(Options options, Dictionary<uint, List<string>> packageMap)
    {
        var pin = Environment.GetEnvironmentVariable("CREDUI_SMOKE_PIN");
        if (string.IsNullOrEmpty(pin) && !options.NoSubmit)
        {
            Console.Error.WriteLine("Set CREDUI_SMOKE_PIN first, or pass --no-submit to stop at the PIN field.");
            Console.Error.WriteLine("A wrong PIN counts against Windows Hello's own failure counter, so the");
            Console.Error.WriteLine("harness will not guess one.");
            return 1;
        }

        return DriveAndSubmit(
            options,
            packageMap,
            dialog =>
            {
                var more = Ui.FindByNameContains(dialog, "More choices", "More options");
                if (more is not null)
                {
                    Ui.Invoke(more);
                    Thread.Sleep(900);
                }

                var tile = Tiles(dialog).FirstOrDefault(t =>
                {
                    var name = t.Current.Name ?? string.Empty;
                    return name.Contains("PIN", StringComparison.OrdinalIgnoreCase)
                           || name.Contains("Hello", StringComparison.OrdinalIgnoreCase);
                });

                if (tile is null)
                {
                    Console.Error.WriteLine("No PIN / Windows Hello tile is offered under More choices.");
                    Console.WriteLine("--- tiles seen ---");
                    foreach (var seen in Tiles(dialog))
                    {
                        Console.WriteLine($"  '{seen.Current.Name}'");
                    }

                    return false;
                }

                Console.WriteLine($"Selecting tile '{tile.Current.Name}' ...");
                if (!Ui.Invoke(tile))
                {
                    Ui.Focus(tile);
                    Ui.SendVirtualKey(Native.VK_RETURN);
                }

                Thread.Sleep(1200);

                Screenshot.Capture(dialog, "pin-tile");
                Console.WriteLine("--- tree on the PIN tile ---");
                Console.WriteLine(Ui.DumpTree(dialog, includeRects: true));

                var field = Ui.FindPasswordField(dialog);
                if (field is null || !Ui.Focus(field))
                {
                    Console.Error.WriteLine("No PIN entry field appeared after choosing the tile.");
                    return false;
                }

                if (options.NoSubmit)
                {
                    Console.WriteLine("--no-submit: reached the PIN field, stopping short of submitting.");
                    return false;
                }

                Ui.TypeText(pin!);
                Console.WriteLine($"Typed PIN ({pin!.Length} characters, not shown).");
                Thread.Sleep(300);
                return ClickOk(dialog);
            });
    }

    /// <summary>
    ///     A picture of the dialog, and nothing else. The automation tree describes the elements
    ///     credui exposes; only a capture shows what was actually drawn, which is where the peek
    ///     glyph and the provider tiles live. Nobody has to be at the keyboard: the prompt is
    ///     cancelled as soon as the PNGs are on disk.
    /// </summary>
    private static int Shot(Options options, Dictionary<uint, List<string>> packageMap)
    {
        var runner = NewRunner(options);
        Console.WriteLine(Header(options));
        runner.Start();

        var dialog = Ui.WaitForDialog(Environment.ProcessId, options.Label, TimeSpan.FromSeconds(25));
        if (dialog is null)
        {
            return NotFound(runner, packageMap);
        }

        // Let the credential provider finish drawing itself, and put it in front: a capture of a
        // half-painted or occluded dialog is worse than none.
        Thread.Sleep(900);
        Focus(dialog);
        Thread.Sleep(200);
        Screenshot.Capture(dialog, "dialog");

        if (options.TypeProbe)
        {
            // The peek glyph is only templated once the password box has something to reveal, so
            // an empty box is not a picture of the peek affordance.
            var field = Ui.FindPasswordField(dialog);
            if (field is not null && Ui.Focus(field))
            {
                Ui.TypeText("probe");
                Thread.Sleep(500);
                Screenshot.Capture(dialog, "password-typed");
            }
        }

        if (options.More)
        {
            var more = Ui.FindByNameContains(dialog, "More choices", "More options");
            if (more is null)
            {
                Console.WriteLine("No \"More choices\" affordance is present.");
            }
            else
            {
                if (!Ui.Invoke(more))
                {
                    Ui.Focus(more);
                    Ui.SendVirtualKey(Native.VK_RETURN);
                }

                Thread.Sleep(900);
                Screenshot.Capture(dialog, "more-choices");
            }
        }

        if (options.Steps.Count > 0 && Steps.Run(dialog, options.Steps) == StepOutcome.Submitted)
        {
            ClickOk(dialog);
            return Finish(runner, dialog, options, packageMap);
        }

        if (options.Dump)
        {
            Console.WriteLine(Ui.DumpTree(dialog, includeRects: true));
        }

        Console.WriteLine(Ui.Cancel(dialog));
        var closed = runner.Wait(TimeSpan.FromSeconds(10));
        Console.WriteLine(closed ? runner.DescribeOutcome(packageMap) : "The prompt did not return after Cancel.");
        runner.FreeOutputBuffer();
        return closed ? 0 : 3;
    }

    /// <summary>
    ///     The real cmdlet, photographed. Everything else here calls credui directly, which is
    ///     useful for exploring the API and useless as evidence: those commands can raise dialogs
    ///     the module would never raise, so a picture taken that way shows what Windows can draw,
    ///     not what a script gets. This starts a PowerShell, imports the module, calls
    ///     <c>Get-CredUICredential</c> with whatever parameters were asked for, and photographs
    ///     the result - so the flags, the auth-package seed and the message all come from the
    ///     shipping path rather than from this harness.
    /// </summary>
    private static int Cmdlet(Options options)
    {
        var manifest = options.Module ?? FindManifest();
        if (!File.Exists(manifest))
        {
            Console.Error.WriteLine($"No module manifest at '{manifest}'. Pass --module PATH.");
            return 1;
        }

        // The window is found by its message text, so a -Message the harness does not know about
        // is a dialog it cannot find. Say so now rather than time out looking for the wrong text.
        if (options.Args.Contains("-Message", StringComparison.OrdinalIgnoreCase) && !options.LabelWasGiven)
        {
            Console.Error.WriteLine(
                "--args passes -Message, so the dialog will not say the cmdlet's default and cannot be " +
                "found by it. Pass --label with the same text.");
            return 1;
        }

        // Taken before the child starts, so a dialog stranded by an earlier run cannot be mistaken
        // for this one. That matters most for a bare call, where the message is the cmdlet's own
        // default and every such dialog on the desktop looks identical.
        var alreadyOpen = Ui.CredentialWindowHandles();

        var runner = new CmdletRunner { ManifestPath = manifest, Arguments = options.Args };
        Console.WriteLine($"module={manifest}");
        Console.WriteLine($"Get-CredUICredential {(options.Args.Length == 0 ? "<no parameters>" : options.Args)}");
        Console.WriteLine($"waiting up to {options.Timeout.TotalSeconds:0}s for a dialog showing '{options.Label}' ...");
        runner.Start();
        Console.WriteLine($"child PowerShell pid={runner.ProcessId}");

        var dialog = Ui.WaitForForeignDialog(options.Label, options.Timeout, alreadyOpen);
        if (dialog is null)
        {
            Console.Error.WriteLine($"No new credential dialog showing '{options.Label}' turned up.");
            runner.Kill();
            Console.WriteLine(runner.ReadReport());
            return 2;
        }

        // Let the credential provider finish drawing, and put it in front. SendInput goes to the
        // foreground window, so typing at a dialog that is not in front types at something else.
        Thread.Sleep(900);
        Focus(dialog);
        Thread.Sleep(200);
        Screenshot.Capture(dialog, "opened");

        if (options.Dump)
        {
            Console.WriteLine(Ui.DumpTree(dialog, includeRects: true));
        }

        var outcome = options.Steps.Count == 0
            ? StepOutcome.Finished
            : Steps.Run(dialog, options.Steps);

        if (outcome == StepOutcome.Submitted)
        {
            ClickOk(dialog);
        }
        else
        {
            // Anything else, including a script that simply ran out, leaves a modal dialog nobody
            // is there to dismiss. Cancel is the only responsible default.
            Console.WriteLine(Ui.Cancel(dialog));
        }

        if (!runner.Wait(TimeSpan.FromSeconds(20)))
        {
            Console.Error.WriteLine("The cmdlet has not returned; killing the child PowerShell.");
            runner.Kill();
            Console.WriteLine(runner.ReadReport());
            return 3;
        }

        Console.WriteLine("=== cmdlet result ===");
        Console.WriteLine(runner.ReadReport());
        return outcome == StepOutcome.Failed ? 5 : 0;
    }

    /// <summary>
    ///     The manifest above the harness, found by walking up rather than by counting directories,
    ///     so it survives a change of target framework or configuration in the output path.
    /// </summary>
    private static string FindManifest()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "CredUICredential.psd1");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return "CredUICredential.psd1";
    }

    /// <summary>
    ///     The one-shot human diagnostic: raise the prompt, let somebody submit it however they
    ///     like, and report what credui handed back. Cancels itself if nobody turns up.
    /// </summary>
    private static int Submit(Options options, Dictionary<uint, List<string>> packageMap)
    {
        var runner = NewRunner(options);
        Console.WriteLine(Header(options));
        Console.WriteLine($"WAITING FOR A HUMAN: submit the dialog within {options.Timeout.TotalSeconds:0}s.");
        Console.WriteLine("Use a password, or More choices -> PIN, whichever you are testing.");
        runner.Start();

        var dialog = Ui.WaitForDialog(Environment.ProcessId, options.Label, TimeSpan.FromSeconds(25));
        if (dialog is null)
        {
            return NotFound(runner, packageMap);
        }

        Thread.Sleep(600);
        Screenshot.Capture(dialog, "waiting-for-human");
        if (options.Dump)
        {
            Console.WriteLine(Ui.DumpTree(dialog, includeRects: true));
            ReportPasswordSurface(dialog);
        }

        return Finish(runner, dialog, options, packageMap);
    }

    /// <summary>
    ///     Drives a credential dialog belonging to somebody else - normally the PowerShell session
    ///     running the real cmdlet - so the whole module can be smoke-tested end to end without a
    ///     human. This process never sees the resulting credential; the cmdlet does.
    /// </summary>
    private static int Drive(Options options)
    {
        var dialog = Ui.WaitForForeignDialog(options.Label, options.Timeout);
        if (dialog is null)
        {
            Console.Error.WriteLine($"No credential dialog showing '{options.Label}' turned up.");
            Console.WriteLine("--- top-level windows ---");
            try
            {
                foreach (AutomationElement window in AutomationElement.RootElement.FindAll(
                             TreeScope.Children, Condition.TrueCondition))
                {
                    if (!string.IsNullOrWhiteSpace(window.Current.Name))
                    {
                        Console.WriteLine($"  pid={window.Current.ProcessId,-6} '{window.Current.Name}'");
                    }
                }
            }
            catch (ElementNotAvailableException)
            {
            }

            return 2;
        }

        Thread.Sleep(700);
        Focus(dialog);
        Screenshot.Capture(dialog, "foreign-dialog");
        Console.WriteLine($"--- dialog showing '{options.Label}' (window owned by pid {dialog.Current.ProcessId}) ---");
        Console.WriteLine(Ui.DumpTree(dialog, includeRects: true));

        // A step script says exactly what to do to somebody else's dialog, so it replaces the
        // built-in "type the password and click OK" rather than running before it.
        if (options.Steps.Count > 0)
        {
            var outcome = Steps.Run(dialog, options.Steps);
            if (outcome == StepOutcome.Submitted)
            {
                ClickOk(dialog);
                return 0;
            }

            Console.WriteLine(Ui.Cancel(dialog));
            return outcome == StepOutcome.Failed ? 5 : 0;
        }

        if (options.Cancel)
        {
            Console.WriteLine(Ui.Cancel(dialog));
            return 0;
        }

        var password = Environment.GetEnvironmentVariable("CREDUI_SMOKE_PASSWORD");
        if (string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine("Set CREDUI_SMOKE_PASSWORD, or pass --cancel. It is never printed.");
            Console.WriteLine(Ui.Cancel(dialog));
            return 1;
        }

        var passwordField = Ui.FindPasswordField(dialog);
        if (passwordField is null)
        {
            Console.Error.WriteLine("No password field on that dialog.");
            Console.WriteLine(Ui.Cancel(dialog));
            return 5;
        }

        if (string.IsNullOrEmpty(options.Seed))
        {
            var userField = Ui.FindUserNameField(dialog);
            if (userField is not null && Ui.Focus(userField))
            {
                Ui.TypeText(options.User ?? Environment.UserName);
            }
        }

        if (!Ui.Focus(passwordField))
        {
            Console.Error.WriteLine("Could not focus the password field.");
            Console.WriteLine(Ui.Cancel(dialog));
            return 5;
        }

        Ui.TypeText(password);
        Thread.Sleep(300);
        Screenshot.Capture(dialog, "foreign-filled");
        Console.WriteLine("--- password surface with the password typed ---");
        ReportPasswordSurface(dialog);
        ClickOk(dialog);
        return 0;
    }

    private static int DriveAndSubmit(
        Options options,
        Dictionary<uint, List<string>> packageMap,
        Func<AutomationElement, bool> drive)
    {
        var runner = NewRunner(options);
        Console.WriteLine(Header(options));
        runner.Start();

        var dialog = Ui.WaitForDialog(Environment.ProcessId, options.Label, TimeSpan.FromSeconds(25));
        if (dialog is null)
        {
            return NotFound(runner, packageMap);
        }

        Thread.Sleep(800);
        try
        {
            var handle = new IntPtr(dialog.Current.NativeWindowHandle);
            if (handle != IntPtr.Zero)
            {
                Native.SetForegroundWindow(handle);
                Thread.Sleep(200);
            }
        }
        catch (ElementNotAvailableException)
        {
        }

        Screenshot.Capture(dialog, "opened");
        if (options.Dump)
        {
            Console.WriteLine(Ui.DumpTree(dialog, includeRects: true));
        }

        if (!drive(dialog))
        {
            Console.WriteLine(Ui.Cancel(dialog));
            runner.Wait(TimeSpan.FromSeconds(10));
            Console.WriteLine(runner.DescribeOutcome(packageMap));
            runner.FreeOutputBuffer();
            return 5;
        }

        return Finish(runner, dialog, options, packageMap);
    }

    private static int Finish(
        PromptRunner runner,
        AutomationElement dialog,
        Options options,
        Dictionary<uint, List<string>> packageMap)
    {
        var timedOut = false;
        if (!runner.Wait(options.Timeout))
        {
            timedOut = true;
            Console.WriteLine($"Timed out after {options.Timeout.TotalSeconds:0}s. {Ui.Cancel(dialog)}");
            if (!runner.Wait(TimeSpan.FromSeconds(10)))
            {
                Console.Error.WriteLine("The prompt still has not returned; leaving it to the watchdog.");
                return 3;
            }
        }

        Console.WriteLine("=== RESULT ===");
        Console.WriteLine(runner.DescribeOutcome(packageMap));
        runner.FreeOutputBuffer();
        return timedOut ? 3 : runner.Failure is null ? 0 : 5;
    }

    /// <summary>
    ///     No window turned up. Usually that means the prompt failed outright rather than that UI
    ///     Automation missed it, so say which - and list what was on the desktop either way.
    /// </summary>
    private static int NotFound(PromptRunner runner, Dictionary<uint, List<string>> packageMap)
    {
        Console.Error.WriteLine("The dialog never appeared.");
        Console.WriteLine("--- top-level windows at that moment ---");
        try
        {
            foreach (AutomationElement window in AutomationElement.RootElement.FindAll(
                         TreeScope.Children, Condition.TrueCondition))
            {
                var name = window.Current.Name ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    Console.WriteLine($"  pid={window.Current.ProcessId,-6} '{name}' class='{window.Current.ClassName}'");
                }
            }
        }
        catch (ElementNotAvailableException)
        {
        }

        Ui.Cancel(null);
        var returned = runner.Wait(TimeSpan.FromSeconds(5));
        Console.WriteLine(returned
            ? runner.DescribeOutcome(packageMap)
            : "The prompt call has still not returned, so a window is up somewhere.");
        runner.FreeOutputBuffer();
        return 2;
    }

    private static bool ClickOk(AutomationElement dialog)
    {
        var ok = dialog.FindFirst(
            TreeScope.Descendants,
            new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.NameProperty, "OK")));
        if (ok is not null && Ui.Invoke(ok))
        {
            Console.WriteLine("Clicked OK.");
            return true;
        }

        Ui.SendVirtualKey(Native.VK_RETURN);
        Console.WriteLine("Pressed Enter (no OK button could be invoked).");
        return true;
    }

    private static void Focus(AutomationElement dialog)
    {
        try
        {
            var handle = new IntPtr(dialog.Current.NativeWindowHandle);
            if (handle != IntPtr.Zero)
            {
                Native.SetForegroundWindow(handle);
                Thread.Sleep(150);
            }
        }
        catch (ElementNotAvailableException)
        {
        }
    }

    private static void ReportPasswordSurface(AutomationElement dialog)
    {
        var passwordField = Ui.FindPasswordField(dialog);
        Console.WriteLine($"password field present: {passwordField is not null}");
        if (passwordField is null)
        {
            return;
        }

        var candidates = Ui.PeekCandidates(dialog);
        Console.WriteLine($"peek-glyph candidates inside the password box: {candidates.Length}");
        foreach (var candidate in candidates)
        {
            Console.WriteLine($"  name='{candidate.Current.Name}' id='{candidate.Current.AutomationId}' " +
                              $"class='{candidate.Current.ClassName}'");
        }

        var more = Ui.FindByNameContains(dialog, "More choices", "More options");
        Console.WriteLine($"\"More choices\" present: {more is not null}" +
                          (more is null ? string.Empty : $" (as {Trim(more.Current.ControlType.ProgrammaticName)})"));
    }

    private static IEnumerable<AutomationElement> Tiles(AutomationElement dialog)
        => Ui.Descendants(dialog)
            .Where(e => e.Current.ControlType == ControlType.ListItem
                        || e.Current.ControlType == ControlType.RadioButton
                        || (e.Current.ControlType == ControlType.Button
                            && !string.IsNullOrWhiteSpace(e.Current.Name)
                            && e.Current.Name is not ("OK" or "Cancel")));

    private static PromptRunner NewRunner(Options options) => new()
    {
        Flags = options.Flags,
        InputAuthPackage = options.InPackage,
        SeedUserName = options.Seed,
        AuthError = options.AuthError,
        Caption = "CredUiSmoke",
        Message = options.Label,
    };

    private static string Header(Options options)
        => $"command={options.Command} flags=0x{options.Flags:X} ({DescribeFlags(options.Flags)}) " +
           $"inAuthPackage={options.InPackage} seedUser={(options.Seed ?? "<none>")} " +
           $"authError={options.AuthError} timeout={options.Timeout.TotalSeconds:0}s label='{options.Label}'";

    private static string DescribeFlags(int flags)
    {
        var names = new List<string>();
        void Add(int flag, string name)
        {
            if ((flags & flag) == flag)
            {
                names.Add(name);
            }
        }

        Add(Native.CREDUIWIN_GENERIC, "GENERIC");
        Add(Native.CREDUIWIN_CHECKBOX, "CHECKBOX");
        Add(Native.CREDUIWIN_AUTHPACKAGE_ONLY, "AUTHPACKAGE_ONLY");
        Add(Native.CREDUIWIN_IN_CRED_ONLY, "IN_CRED_ONLY");
        Add(Native.CREDUIWIN_ENUMERATE_ADMINS, "ENUMERATE_ADMINS");
        Add(Native.CREDUIWIN_ENUMERATE_CURRENT_USER, "ENUMERATE_CURRENT_USER");
        Add(Native.CREDUIWIN_SECURE_PROMPT, "SECURE_PROMPT");
        Add(Native.CREDUIWIN_PREPROMPTING, "PREPROMPTING");
        Add(Native.CREDUIWIN_PACK_32_WOW, "PACK_32_WOW");
        return names.Count == 0 ? "none" : string.Join("|", names);
    }

    private static string Trim(string programmaticName)
        => programmaticName.StartsWith("ControlType.", StringComparison.Ordinal)
            ? programmaticName["ControlType.".Length..]
            : programmaticName;

    private sealed class Options
    {
        internal string Command { get; private set; } = string.Empty;
        internal int Flags { get; private set; } = Native.CREDUIWIN_AUTHPACKAGE_ONLY | Native.CREDUIWIN_CHECKBOX;
        internal uint InPackage { get; private set; }
        internal string? Seed { get; private set; }
        internal string? User { get; private set; }
        internal int AuthError { get; private set; }
        internal TimeSpan Timeout { get; private set; }
        internal bool More { get; private set; }
        internal bool Dump { get; private set; }
        internal bool Shot { get; private set; }
        internal bool ShotScreen { get; private set; }
        internal string? ShotDir { get; private set; }
        internal bool TypeProbe { get; private set; }
        internal bool NoSubmit { get; private set; }
        internal bool Cancel { get; private set; }
        internal int Pid { get; private set; }
        internal string Args { get; private set; } = string.Empty;
        internal string? Module { get; private set; }
        internal IReadOnlyList<Step> Steps { get; private set; } = Array.Empty<Step>();

        /// <summary>
        ///     Whether --label was passed. `cmdlet` needs to tell "the caller wants the default
        ///     message" from "the caller named the message", because only the second is compatible
        ///     with a -Message in --args.
        /// </summary>
        internal bool LabelWasGiven { get; private set; }

        // Unique per run: the harness finds its own window by this text, and a credential dialog
        // stranded by an earlier probe is otherwise indistinguishable from ours.
        internal string Label { get; private set; } = $"CredUiSmoke probe {Environment.ProcessId}";

        internal static Options? Parse(string[] args, out string? error)
        {
            error = null;
            var options = new Options { Command = args[0] };
            string? pendingPackageName = null;
            var stepScript = new List<string>();
            var timeoutSeconds = options.Command == "submit" ? 300 : 45;

            if (options.Command == "cmdlet")
            {
                // The cmdlet chooses the message when the caller does not, so that is the text its
                // window will be carrying.
                options.Label = CmdletRunner.DefaultMessage;
            }

            for (var i = 1; i < args.Length; i++)
            {
                var argument = args[i];
                string Next()
                {
                    if (i + 1 >= args.Length)
                    {
                        throw new ArgumentException($"{argument} needs a value.");
                    }

                    return args[++i];
                }

                try
                {
                    switch (argument)
                    {
                        case "--flags":
                            options.Flags = ParseNumber(Next());
                            break;
                        case "--in-package":
                            var value = Next();
                            if (int.TryParse(value, out var numeric))
                            {
                                options.InPackage = (uint)numeric;
                            }
                            else
                            {
                                pendingPackageName = value;
                            }

                            break;
                        case "--user":
                            options.User = Next();
                            options.Seed = options.User;
                            break;
                        case "--no-seed":
                            options.Seed = null;
                            break;
                        case "--auth-error":
                            options.AuthError = ParseNumber(Next());
                            break;
                        case "--timeout":
                            timeoutSeconds = ParseNumber(Next());
                            break;
                        case "--more":
                            options.More = true;
                            break;
                        case "--dump":
                            options.Dump = true;
                            break;
                        case "--shot":
                            options.Shot = true;
                            break;
                        case "--shot-screen":
                            options.Shot = true;
                            options.ShotScreen = true;
                            break;
                        case "--shot-dir":
                            options.ShotDir = Next();
                            options.Shot = true;
                            break;
                        case "--type-probe":
                            options.TypeProbe = true;
                            break;
                        case "--no-submit":
                            options.NoSubmit = true;
                            break;
                        case "--cancel":
                            options.Cancel = true;
                            break;
                        case "--pid":
                            options.Pid = ParseNumber(Next());
                            break;
                        case "--label":
                            options.Label = Next();
                            options.LabelWasGiven = true;
                            break;
                        case "--args":
                            options.Args = Next();
                            break;
                        case "--module":
                            options.Module = Next();
                            break;
                        case "--step":
                            stepScript.Add(Next());
                            break;
                        default:
                            error = $"Unknown option '{argument}'.";
                            return null;
                    }
                }
                catch (ArgumentException exception)
                {
                    error = exception.Message;
                    return null;
                }
            }

            if (pendingPackageName is not null)
            {
                var map = Diagnostics.LookupPackages(out _);
                var match = map.FirstOrDefault(p => p.Value.Contains(pendingPackageName, StringComparer.OrdinalIgnoreCase));
                if (match.Value is null)
                {
                    error = $"LSA does not know an authentication package called '{pendingPackageName}'.";
                    return null;
                }

                options.InPackage = match.Key;
            }

            var steps = CredUiSmoke.Steps.Parse(stepScript, out var stepError);
            if (steps is null)
            {
                error = stepError;
                return null;
            }

            options.Steps = steps;
            options.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            return options;
        }

        private static int ParseNumber(string text)
            => text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToInt32(text[2..], 16)
                : int.Parse(text);
    }
}
