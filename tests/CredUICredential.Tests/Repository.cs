using System;
using System.IO;

namespace CredUICredential.Tests
{
    /// <summary>
    ///     Locates the files that get shipped, so tests can check the module as it is published
    ///     rather than only the assembly as it is compiled.
    /// </summary>
    internal static class Repository
    {
        private static readonly Lazy<DirectoryInfo> RootDirectory = new(FindRoot);

        public static DirectoryInfo Root => RootDirectory.Value;

        public static FileInfo File(string relativePath)
        {
            var file = new FileInfo(Path.Combine(Root.FullName, relativePath));
            if (!file.Exists)
            {
                throw new FileNotFoundException(
                    $"'{relativePath}' is not in the repository at '{Root.FullName}'.", file.FullName);
            }

            return file;
        }

        private static DirectoryInfo FindRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory != null;
                 directory = directory.Parent)
            {
                if (System.IO.File.Exists(Path.Combine(directory.FullName, "CredUICredential.psd1")))
                {
                    return directory;
                }
            }

            throw new DirectoryNotFoundException(
                $"No CredUICredential.psd1 above '{AppContext.BaseDirectory}'.");
        }
    }
}
