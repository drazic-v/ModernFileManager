using ReactiveUI;
using System;
using System.Reactive;
using System.Threading;

namespace FileManager.App.ViewModels;

/// <summary>One paste as shown in the transfers area. Cancel() is public so the conflict
/// tracker can abandon the whole paste when the dialog is dismissed.</summary>
public class TransferViewModel : ReactiveObject, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private double _progressPercent;
    private bool _isMeasuring = true;
    public bool IsMeasuring
    {
        get => _isMeasuring;
        set => this.RaiseAndSetIfChanged(ref _isMeasuring, value);
    }

    public TransferViewModel(string name)
    {
        Name = name;
        CancelCommand = ReactiveCommand.Create(Cancel);
    }

    public string Name { get; }
    public CancellationToken Token => _cts.Token;

    public double ProgressPercent
    {
        get => _progressPercent;
        set => this.RaiseAndSetIfChanged(ref _progressPercent, value);
    }

    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public void Cancel()
    {
        try { _cts.Cancel(); }
        catch (ObjectDisposedException) { }   // already finished and disposed
    }

    public void Dispose() => _cts.Dispose();
}