using System.Windows.Automation;

namespace CredUiSmoke;

internal enum StepOutcome
{
    /// <summary>The script ran out. Whoever asked for it decides what to do with the dialog.</summary>
    Finished,

    /// <summary>A step clicked OK.</summary>
    Submitted,

    /// <summary>A step cancelled the dialog.</summary>
    Cancelled,

    /// <summary>A step could not find what it was aimed at, and the rest was abandoned.</summary>
    Failed,
}

internal sealed record Step(string Verb, string Argument);

/// <summary>
///     An ordered script of things to do to the dialog before, between and after captures. A
///     screenshot of an untouched prompt only ever shows one of the states the dialog has; the
///     interesting ones - a typed user name, the tiles under "More choices", a revealed password -
///     exist only after somebody has interacted with it, and there is nobody at the keyboard.
/// </summary>
internal static class Steps
{
    internal const string Help = """
          shot[:TAG]        Capture the dialog now. TAG names the file; defaults to the step number.
          wait:MS           Sleep, for animations and for a tile that redraws itself.
          focus:WHERE       Put focus somewhere. WHERE is `user`, `password`, or any fragment of an
                            element's name.
          type:TEXT         Type TEXT into whatever has focus. Only the length is reported.
          secret:VARIABLE   Type the contents of an environment variable, which is never printed.
          clear             Empty the focused field (Ctrl+A, Delete), to type over a seeded value.
          key:NAME          Press a key: tab, enter, esc, back, space, home, end, delete.
          click:NAME        Click the element whose name contains NAME, e.g. `click:More choices`.
          peek[:MS]         Press and HOLD the password box's reveal glyph, capture while it is
                            held, then let go. MS is extra dwell before the capture.
                            The capture shows the typed password in clear text - that is the point,
                            so ask for it deliberately.
          dump              Dump the UI Automation tree.
          surface           Report the password field, the peek candidates and "More choices".
          ok                Click OK. Ends the script.
          cancel            Cancel the dialog. Ends the script.
        """;

    internal static IReadOnlyList<Step>? Parse(IEnumerable<string> raw, out string? error)
    {
        error = null;
        var steps = new List<Step>();
        foreach (var text in raw)
        {
            var split = text.Split(':', 2);
            var verb = split[0].Trim().ToLowerInvariant();
            var argument = split.Length > 1 ? split[1] : string.Empty;

            if (!KnownVerbs.Contains(verb))
            {
                error = $"'{verb}' is not a step. Known steps: {string.Join(", ", KnownVerbs)}.";
                return null;
            }

            if (RequireArgument.Contains(verb) && argument.Length == 0)
            {
                error = $"The '{verb}' step needs an argument, as in --step {verb}:something.";
                return null;
            }

            steps.Add(new Step(verb, argument));
        }

        return steps;
    }

    private static readonly HashSet<string> KnownVerbs = new(StringComparer.Ordinal)
    {
        "shot", "wait", "focus", "type", "secret", "clear", "key", "click", "peek", "dump",
        "surface", "ok", "cancel",
    };

    private static readonly HashSet<string> RequireArgument = new(StringComparer.Ordinal)
    {
        "wait", "focus", "type", "secret", "key", "click",
    };

    internal static StepOutcome Run(AutomationElement dialog, IReadOnlyList<Step> steps)
    {
        var focused = false;
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var number = index + 1;
            Console.WriteLine($"step {number}/{steps.Count}: {Describe(step)}");

            switch (step.Verb)
            {
                case "shot":
                    Screenshot.Capture(dialog, step.Argument.Length == 0 ? $"step{number:00}" : step.Argument);
                    break;

                case "wait":
                    if (!int.TryParse(step.Argument, out var milliseconds))
                    {
                        Console.Error.WriteLine($"  '{step.Argument}' is not a number of milliseconds.");
                        return StepOutcome.Failed;
                    }

                    Thread.Sleep(Math.Clamp(milliseconds, 0, 30_000));
                    break;

                case "focus":
                    if (!DoFocus(dialog, step.Argument))
                    {
                        return StepOutcome.Failed;
                    }

                    focused = true;
                    break;

                case "type":
                case "secret":
                    // Typing goes to whatever holds focus, which is a window, not a field. If a
                    // focus step failed silently the characters would land somewhere else on the
                    // desktop - so an unfocused script is worth saying out loud, and a failed
                    // focus stops the run outright rather than typing a password into the void.
                    if (!focused)
                    {
                        Console.WriteLine("  NOTE: no focus step has run, so this goes wherever the dialog left focus.");
                    }

                    if (!DoType(step))
                    {
                        return StepOutcome.Failed;
                    }

                    break;

                case "clear":
                    Ui.ClearFocusedField();
                    break;

                case "key":
                    if (!DoKey(step.Argument))
                    {
                        return StepOutcome.Failed;
                    }

                    break;

                case "click":
                    if (!DoClick(dialog, step.Argument))
                    {
                        return StepOutcome.Failed;
                    }

                    break;

                case "peek":
                    if (!DoPeek(dialog, step.Argument))
                    {
                        return StepOutcome.Failed;
                    }

                    break;

                case "dump":
                    Console.WriteLine(Ui.DumpTree(dialog, includeRects: true));
                    break;

                case "surface":
                    ReportSurface(dialog);
                    break;

                case "ok":
                    return StepOutcome.Submitted;

                case "cancel":
                    return StepOutcome.Cancelled;
            }
        }

        return StepOutcome.Finished;
    }

    private static bool DoFocus(AutomationElement dialog, string where)
    {
        var target = where.ToLowerInvariant() switch
        {
            "user" or "username" or "user-name" => Ui.FindUserNameField(dialog),
            "password" or "pass" => Ui.FindPasswordField(dialog),
            _ => Ui.FindByNameContains(dialog, where),
        };

        if (target is null)
        {
            Console.Error.WriteLine($"  nothing on the dialog matches '{where}'.");
            return false;
        }

        if (Ui.Focus(target))
        {
            return true;
        }

        // Some credential-provider fields refuse SetFocus but take a pointer click happily.
        Console.WriteLine("  SetFocus was refused; clicking the field instead.");
        return Ui.MouseClick(target);
    }

    private static bool DoType(Step step)
    {
        var text = step.Verb == "secret"
            ? Environment.GetEnvironmentVariable(step.Argument)
            : step.Argument;

        if (step.Verb == "secret" && string.IsNullOrEmpty(text))
        {
            Console.Error.WriteLine($"  the environment variable {step.Argument} is not set.");
            return false;
        }

        Ui.TypeText(text!);
        Console.WriteLine($"  typed {text!.Length} characters.");
        return true;
    }

    private static bool DoKey(string name)
    {
        var key = name.ToLowerInvariant() switch
        {
            "tab" => Native.VK_TAB,
            "enter" or "return" => Native.VK_RETURN,
            "esc" or "escape" => Native.VK_ESCAPE,
            "back" or "backspace" => Native.VK_BACK,
            "space" => Native.VK_SPACE,
            "home" => Native.VK_HOME,
            "end" => Native.VK_END,
            "delete" or "del" => Native.VK_DELETE,
            _ => (ushort)0,
        };

        if (key == 0)
        {
            Console.Error.WriteLine($"  '{name}' is not a key this harness knows how to press.");
            return false;
        }

        Ui.SendVirtualKey(key);
        Thread.Sleep(120);
        return true;
    }

    private static bool DoClick(AutomationElement dialog, string name)
    {
        var target = Ui.FindByNameContains(dialog, name);
        if (target is null)
        {
            Console.Error.WriteLine($"  nothing on the dialog is named like '{name}'.");
            return false;
        }

        Console.WriteLine($"  clicking '{target.Current.Name}'.");
        if (Ui.Invoke(target))
        {
            Thread.Sleep(600);
            return true;
        }

        // A tile that exposes neither Invoke nor Select still answers to a pointer.
        if (Ui.MouseClick(target))
        {
            Thread.Sleep(600);
            return true;
        }

        Ui.Focus(target);
        Ui.SendVirtualKey(Native.VK_RETURN);
        Thread.Sleep(600);
        return true;
    }

    /// <summary>
    ///     The reveal glyph is hold-to-show, so the capture has to happen while the button is
    ///     still down. Nothing else in the harness produces a picture of a password in the clear,
    ///     and that is deliberate: it takes an explicit step to ask for one.
    /// </summary>
    private static bool DoPeek(AutomationElement dialog, string argument)
    {
        var reveal = Ui.FindRevealButton(dialog);
        if (reveal is null)
        {
            Console.Error.WriteLine(
                "  no reveal glyph on the password box. It is only templated once the box has " +
                "something to reveal, so type into it before peeking.");
            return false;
        }

        var dwell = int.TryParse(argument, out var parsed) ? Math.Clamp(parsed, 0, 5_000) : 0;
        Console.WriteLine(
            $"  holding '{reveal.Current.Name}' (id='{reveal.Current.AutomationId}'); " +
            "the capture taken while it is down SHOWS THE PASSWORD IN CLEAR TEXT.");

        var held = Ui.PressAndHold(reveal, () =>
        {
            if (dwell > 0)
            {
                Thread.Sleep(dwell);
            }

            Screenshot.Capture(dialog, "peek");
            ReportToggleState(reveal);
        });

        if (!held)
        {
            Console.Error.WriteLine("  the reveal glyph reported no rectangle to press.");
            return false;
        }

        return true;
    }

    /// <summary>
    ///     What the toggle pattern makes of the hold. Observed to stay <c>Off</c> on Windows 11
    ///     while the glyph is plainly revealing the password, so this is a note and not a check:
    ///     the reveal is a visual state the pattern does not track, which is the whole reason a
    ///     capture is worth more here than a tree dump.
    /// </summary>
    private static void ReportToggleState(AutomationElement reveal)
    {
        try
        {
            if (reveal.TryGetCurrentPattern(TogglePattern.Pattern, out var pattern))
            {
                Console.WriteLine(
                    $"  reveal toggle state while held: {((TogglePattern)pattern).Current.ToggleState} " +
                    "(the pattern does not track the hold - the capture is the evidence, not this line)");
            }
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException)
        {
        }
    }

    private static void ReportSurface(AutomationElement dialog)
    {
        var passwordField = Ui.FindPasswordField(dialog);
        Console.WriteLine($"  password field present: {passwordField is not null}");
        foreach (var candidate in Ui.PeekCandidates(dialog))
        {
            Console.WriteLine($"  peek candidate: name='{candidate.Current.Name}' " +
                              $"id='{candidate.Current.AutomationId}' class='{candidate.Current.ClassName}'");
        }

        var more = Ui.FindByNameContains(dialog, "More choices", "More options");
        Console.WriteLine($"  \"More choices\" present: {more is not null}");
    }

    /// <summary>
    ///     What a step is about to do, without saying what it is about to type. A literal typed on
    ///     the command line is still a password as far as the console log is concerned.
    /// </summary>
    private static string Describe(Step step) => step.Verb switch
    {
        "type" => $"type ({step.Argument.Length} characters)",
        "secret" => $"secret (from {step.Argument})",
        _ => step.Argument.Length == 0 ? step.Verb : $"{step.Verb}:{step.Argument}",
    };
}
