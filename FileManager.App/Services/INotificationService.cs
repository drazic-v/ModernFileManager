using System;
using System.Collections.Generic;
using System.Text;

namespace FileManager.App.Services;

public interface INotificationService
{
    void ShowSuccess(string message);
    void ShowError(string message);
}
