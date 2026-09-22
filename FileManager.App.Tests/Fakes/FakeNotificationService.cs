using FileManager.App.Services;

namespace FileManager.App.Tests.Fakes;

/// <summary>
/// Captures notification calls instead of touching a real WindowNotificationManager,
/// so tests can assert on what the ViewModel tried to tell the user.
/// </summary>
public sealed class FakeNotificationService : INotificationService
{
    public List<string> Errors { get; } = new();
    public List<string> Successes { get; } = new();

    public void ShowError(string message) => Errors.Add(message);
    public void ShowSuccess(string message) => Successes.Add(message);
}