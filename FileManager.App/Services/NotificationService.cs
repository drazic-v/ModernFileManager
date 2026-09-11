using System;
using Avalonia.Controls.Notifications;

namespace FileManager.App.Services;

public class NotificationService : INotificationService
{
    private readonly WindowNotificationManager _manager;

    public NotificationService(WindowNotificationManager manager) => _manager = manager;

    public void ShowSuccess(string message) =>
        _manager.Show(new Notification("Success", message, NotificationType.Success, TimeSpan.FromSeconds(3)));

    public void ShowError(string message) =>
        _manager.Show(new Notification("Error", message, NotificationType.Error, TimeSpan.FromSeconds(5)));
}