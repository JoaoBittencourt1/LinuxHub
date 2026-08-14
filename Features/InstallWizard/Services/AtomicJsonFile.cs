using System.IO;
using System.Text;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Publishes UTF-8 JSON with a same-directory atomic rename so readers either see the
    /// previous complete document or the new complete document (installation-plan spec).
    /// </summary>
    internal static class AtomicJsonFile
    {
        private const int MaxPublishAttempts = 8;
        private const int ErrorAccessDenied = 5;
        private const int ErrorSharingViolation = 32;
        private const int ErrorLockViolation = 33;
        private const int ErrorUnableToRemoveReplaced = 1175;
        private const int ErrorUnableToMoveReplacement = 1176;
        private const int ErrorUnableToMoveReplacement2 = 1177;
        private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);

        public static void Write(string path, string json)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A JSON file path is required.", nameof(path));
            ArgumentNullException.ThrowIfNull(json);

            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory))
                throw new InvalidOperationException("The JSON file path has no parent directory.");

            Directory.CreateDirectory(directory);
            string temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

            Exception? writeFailure = null;
            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, Utf8WithoutBom))
                {
                    writer.Write(json);
                    writer.Write('\n');
                    writer.Flush();
                    stream.Flush(true);
                }

                Publish(temporaryPath, fullPath);
            }
            catch (Exception exception)
            {
                writeFailure = exception;
                throw;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch (Exception) when (writeFailure is not null)
                    {
                    }
                }
            }
        }

        private static void Publish(string temporaryPath, string fullPath)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(fullPath))
                        File.Replace(temporaryPath, fullPath, null);
                    else
                        File.Move(temporaryPath, fullPath);
                    return;
                }
                catch (Exception exception)
                    when (attempt < MaxPublishAttempts && IsTransientPublishFailure(exception))
                {
                    Thread.Sleep(25 * attempt);
                }
            }
        }

        private static bool IsTransientPublishFailure(Exception exception)
        {
            if (exception is not IOException and not UnauthorizedAccessException)
                return false;

            int win32Code = exception.HResult & 0xFFFF;
            return win32Code is ErrorAccessDenied
                or ErrorSharingViolation
                or ErrorLockViolation
                or ErrorUnableToRemoveReplaced
                or ErrorUnableToMoveReplacement
                or ErrorUnableToMoveReplacement2;
        }
    }
}
