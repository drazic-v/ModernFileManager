using System;
using System.Collections.ObjectModel;
using System.Reactive;
using ReactiveUI;
using FileManager.Core.Models;
using FileManager.Core.Providers;

namespace FileManager.App.ViewModels;

public class WorkspaceViewModel : ReactiveObject
{
    private readonly IStorageProvider _provider;
    private readonly StoragePath _defaultStartingFolder;
    private MainViewModel? _selectedTab;

    public WorkspaceViewModel(IStorageProvider provider, StoragePath startingFolder)
    {
        _provider = provider;
        _defaultStartingFolder = startingFolder;

        Tabs = new ObservableCollection<MainViewModel>
        {
            new MainViewModel(provider, startingFolder)
        };
        SelectedTab = Tabs[0];

        AddTabCommand = ReactiveCommand.Create(AddTab);
        CloseTabCommand = ReactiveCommand.Create<MainViewModel>(CloseTab);
    }

    public ObservableCollection<MainViewModel> Tabs { get; }

    public MainViewModel? SelectedTab
    {
        get => _selectedTab;
        set => this.RaiseAndSetIfChanged(ref _selectedTab, value);
    }

    public ReactiveCommand<Unit, Unit> AddTabCommand { get; }
    public ReactiveCommand<MainViewModel, Unit> CloseTabCommand { get; }

    private void AddTab()
    {
        var tab = new MainViewModel(_provider, _defaultStartingFolder);
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    private void CloseTab(MainViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0) return;

        Tabs.Remove(tab);

        if (Tabs.Count == 0)
        {
            AddTab(); // never let the workspace end up with zero tabs
            return;
        }

        if (SelectedTab == tab)
            SelectedTab = Tabs[Math.Max(0, index - 1)];
    }
}