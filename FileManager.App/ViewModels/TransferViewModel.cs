using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Reactive;
using System.Text;
using System.Threading;

namespace FileManager.App.ViewModels;

// Placeholder shape for one in-progress transfer. Whenever your real upload/download
// logic exists, it just needs to update ProgressPercent as it goes - the bar reacts
// on its own, same as everything else built with RaiseAndSetIfChanged.
public class TransferViewModel : ReactiveObject, IDisposable
{
    private double _progressPercent;
    private readonly CancellationTokenSource _cts = new();
    private bool _isMeasuring = true;
    public bool IsMeasuring
    {
        get => _isMeasuring;
        set => this.RaiseAndSetIfChanged(ref _isMeasuring, value);
    }

    public TransferViewModel(string name)
    {
        Name = name;
        CancelCommand = ReactiveCommand.Create(() => _cts.Cancel());
    }

    public string Name { get; }
    public CancellationToken Token => _cts.Token;

    public double ProgressPercent
    {
        get => _progressPercent;
        set => this.RaiseAndSetIfChanged(ref _progressPercent, value);
    }

    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public void Dispose() => _cts.Dispose();
}