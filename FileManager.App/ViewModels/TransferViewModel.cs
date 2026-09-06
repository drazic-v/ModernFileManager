using System;
using System.Collections.Generic;
using System.Text;
using ReactiveUI;

namespace FileManager.App.ViewModels;

// Placeholder shape for one in-progress transfer. Whenever your real upload/download
// logic exists, it just needs to update ProgressPercent as it goes - the bar reacts
// on its own, same as everything else built with RaiseAndSetIfChanged.
public class TransferViewModel : ReactiveObject
{
    private double _progressPercent;

    public TransferViewModel(string name) => Name = name;

    public string Name { get; }

    public double ProgressPercent
    {
        get => _progressPercent;
        set => this.RaiseAndSetIfChanged(ref _progressPercent, value);
    }
}