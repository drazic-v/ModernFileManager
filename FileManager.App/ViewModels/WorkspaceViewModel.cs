using System;
using System.Collections.ObjectModel;
using System.Reactive;
using ReactiveUI;
using FileManager.Core.Models;
using FileManager.Core.Providers;

namespace FileManager.App.ViewModels;

public class WorkspaceViewModel : ReactiveObject
{
    private MainViewModel? _selectedTab;
    public ObservableCollection<TransferViewModel> ActiveTransfers { get; } = new();

    public WorkspaceViewModel(IStorageProvider provider, StoragePath startingFolder, string displayName)
    {

        //Tabs = new ObservableCollection<MainViewModel>
        //{
        //    new MainViewModel(provider, startingFolder, displayName)
        //};
        Tabs = new ObservableCollection<MainViewModel>();

        //SelectedTab = Tabs[0];
        //UpdateCanCloseTabs();

        AddTabCommand = ReactiveCommand.Create(AddTab);
        CloseTabCommand = ReactiveCommand.Create<MainViewModel>(CloseTab);
        // This is just a placeholder to demonstrate how the transfer progress bar works.
        // In a real application, you would add TransferViewModel instances to
        // ActiveTransfers when actual file transfers are initiated.
        //ActiveTransfers.Add(new TransferViewModel("example.zip") { ProgressPercent = 42 });

        Providers.Add(new ProviderViewModel(displayName, provider, startingFolder));
        OpenProviderCommand = ReactiveCommand.Create<ProviderViewModel>(entry => OpenTab(entry.Provider, entry.StartingFolder, entry.DisplayName));
        AddProviderCommand = ReactiveCommand.Create(() => { /* TODO: open a connect-provider window once a second provider type exists */ });
    }

    public ObservableCollection<MainViewModel> Tabs { get; }

    public ObservableCollection<ProviderViewModel> Providers { get; } = new();

    public MainViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (_selectedTab == value) return;
            if (_selectedTab is not null) _selectedTab.IsActive = false;
            this.RaiseAndSetIfChanged(ref _selectedTab, value);
            if (_selectedTab is not null) _selectedTab.IsActive = true;
        }
    }

    public ReactiveCommand<Unit, Unit> AddTabCommand { get; }
    public ReactiveCommand<MainViewModel, Unit> CloseTabCommand { get; }

    public ReactiveCommand<ProviderViewModel, Unit> OpenProviderCommand { get; }
    public ReactiveCommand<Unit, Unit> AddProviderCommand { get; }


    private void AddTab()
    {
        if (SelectedTab is { } current)
            OpenTab(current.Provider, current.CurrentFolder, current.DisplayName);
        else
            OpenTab(Providers[0].Provider, Providers[0].StartingFolder, Providers[0].DisplayName);
    }
    private void OpenTab(IStorageProvider provider, StoragePath startingFolder, string displayName)
    {
        var tab = new MainViewModel(provider, startingFolder, displayName);
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    private void CloseTab(MainViewModel tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0) return;

        var wasSelected = SelectedTab == tab;

        Tabs.Remove(tab);
        tab.Dispose();

        if (wasSelected)
            SelectedTab = Tabs.Count > 0 ? Tabs[Math.Max(0, index - 1)] : null;
    }
}