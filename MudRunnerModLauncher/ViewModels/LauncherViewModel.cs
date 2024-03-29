﻿using Avalonia.Controls;
using Avalonia.Platform.Storage;
using MudRunnerModLauncher.Models;
using MudRunnerModLauncher.Views;
using ReactiveUI;
using ReactiveUI.Validation.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Models;
using MsBox.Avalonia.Enums;
using Avalonia.Platform;
using Res = MudRunnerModLauncher.Lang.Resource;
using MudRunnerModLauncher.AdditionalWindows;
using System.Text.RegularExpressions;
using Splat.ModeDetection;

namespace MudRunnerModLauncher.ViewModels;

public class LauncherViewModel : ViewModelBase
{
	private readonly Launcher _model = new();
	private List<DirectoryInfo> _addedMods = [];
	private DirectoryInfo? _selectedMod;
	private bool _isBusy;

	public LauncherViewModel() 
	{
		IObservable<bool> validateMRRootDir =
		this.WhenAnyValue(
			x => x.MRRootDirectory,
			x => x.IsCorrectMRRootDir,
			(dir, isCorrect) => string.IsNullOrWhiteSpace(dir) || isCorrect == true);

		this.ValidationRule(vm => vm.MRRootDirectory, validateMRRootDir, Res.WrongPath);


		BrowseMRRootDirCommand = ReactiveCommand.Create(BrowseMRRootDir);

		var canExec = this.WhenAnyValue(vm => vm.SelectedMod, sm => sm as DirectoryInfo != null);
		AddModCommand = ReactiveCommand.Create(AddMod, this.WhenAnyValue(vm => vm.IsCorrectMRRootDir, isCorrect => isCorrect == true));
		DeleteModCommand = ReactiveCommand.Create(DeleteSelectedMod, canExec);
		RenameModCommand = ReactiveCommand.Create(RenameMod, canExec);
		OpenGitHubLinkCommand = ReactiveCommand.Create(OpenGitHubLink);

		this.WhenAnyValue(vm => vm.MRRootDirectory).Subscribe(x => this.RaisePropertyChanged(nameof(IsCorrectMRRootDir)));

		RefreshAddedMods();
	}


	public string MRRootDirectory
	{
		get => _model.MudRunnerRoorDir;
		set
		{
			if(value != _model.MudRunnerRoorDir)
			{
				_model.SaveMudRunnerRoorDir(value);
				this.RaisePropertyChanged(nameof(MRRootDirectory));
				if (_model.IsCorrectMRRootDir)
				{
					RefreshAddedMods();
				}
			}
		}
	}

	public bool IsCorrectMRRootDir => _model.IsCorrectMRRootDir;

	public List<DirectoryInfo> AddedMods
	{
		get => _addedMods;
		set => this.RaiseAndSetIfChanged(ref _addedMods, value, nameof(AddedMods));
	}

	public DirectoryInfo? SelectedMod
	{
		get => _selectedMod;
		set => this.RaiseAndSetIfChanged(ref _selectedMod, value, nameof(SelectedMod));
	}

	public bool IsBusy
	{
		get => _isBusy;
		set => this.RaiseAndSetIfChanged(ref _isBusy, value, nameof(IsBusy));
	}

	public ReactiveCommand<Unit, Task> BrowseMRRootDirCommand { get; private set; }

	public ReactiveCommand<Unit, Task> AddModCommand { get; private set; }

	public ReactiveCommand<Unit, Unit> DeleteModCommand { get; private set; }

	public ReactiveCommand<Unit, Task> RenameModCommand { get; private set; }

	public ReactiveCommand<Unit, Unit> OpenGitHubLinkCommand { get; private set; }


	private async Task BrowseMRRootDir()
	{
		string mRRootDir = await OpenFolderDialog();
		if (!string.IsNullOrWhiteSpace(mRRootDir))
		{
			MRRootDirectory = mRRootDir;
		}
	}

	private async Task AddMod()
	{
		var modPath = await OpenFileDialog();
		if (string.IsNullOrEmpty(modPath))
			return;

		var mod = new FileInfo(modPath);
		var renameRes = await ShowRenameDialog(Path.GetFileNameWithoutExtension(mod.Name));
		if (!renameRes.OK)
			return;

		string modName = renameRes.Text;

		await BusyAction(async () =>
		{
			try
			{			
				await _model.AddModAsync(mod, modName);
			
				if(await _model.IsPresentCache())
				{
					string msbCacheText = string.Format(Res.DeleteCacheFrom, AppPaths.MudRunnerCacheDir);

					var msbCache = MessageBoxManager.GetMessageBoxCustom(
						new MessageBoxCustomParams
						{
							ButtonDefinitions = new List<ButtonDefinition>
							{
								new ButtonDefinition { Name = Res.Yes, },
								new ButtonDefinition { Name = Res.No, },
							},
							WindowIcon = GetLogo(),
							ContentTitle = Res.ModRunnerModLauncherTitle,
							ContentMessage = msbCacheText,
							Icon = Icon.Question,
							WindowStartupLocation = WindowStartupLocation.CenterOwner,
							CanResize = false,
							MinWidth = 350,
							MaxWidth = 500,
							MaxHeight = 800,
							SizeToContent = SizeToContent.WidthAndHeight,
							ShowInCenter = true,
							Topmost = false,
						});

					var res = await msbCache.ShowWindowDialogAsync(MainWindow.Instsnce);
					if (res == Res.Yes)
					{
						await _model.ClearCache();
					}
				}

				RefreshAddedMods();

				var msb = MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
				{
					WindowIcon = GetLogo(),
					ContentTitle = Res.ModRunnerModLauncherTitle,
					ContentMessage = string.Format(Res.ModAdded, modName),
					ButtonDefinitions = ButtonEnum.Ok,
					Icon = Icon.Success,
					WindowStartupLocation = WindowStartupLocation.CenterOwner,
					MinWidth = 350
				});


				await msb.ShowWindowDialogAsync(MainWindow.Instsnce);
			}
			catch (Exception ex)
			{
				var msb = MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
				{
					WindowIcon = GetLogo(),
					ContentTitle = Res.ModRunnerModLauncherTitle,
					ContentMessage = ex.Message,
					ButtonDefinitions = ButtonEnum.Ok,
					Icon = Icon.Error,
					WindowStartupLocation = WindowStartupLocation.CenterOwner,
					MinWidth = 350
				});

				await msb.ShowWindowDialogAsync(MainWindow.Instsnce);
			}

		});
	}

	private async void DeleteSelectedMod()
	{
		await BusyAction(async () =>
		{
			if (SelectedMod == null)
				return;

			string msbCacheText = string.Format(Res.DeleteMod, SelectedMod.Name);

			var msbCache = MessageBoxManager.GetMessageBoxCustom(
				new MessageBoxCustomParams
				{
					ButtonDefinitions = new List<ButtonDefinition>
					{
								new ButtonDefinition { Name = Res.Yes, },
								new ButtonDefinition { Name = Res.No, },
					},
					WindowIcon = GetLogo(),
					ContentTitle = Res.ModRunnerModLauncherTitle,
					ContentMessage = msbCacheText,
					Icon = Icon.Question,
					WindowStartupLocation = WindowStartupLocation.CenterOwner,
					CanResize = false,
					MinWidth = 350,
					MaxWidth = 500,
					MaxHeight = 800,
					SizeToContent = SizeToContent.WidthAndHeight,
					ShowInCenter = true,
					Topmost = false,
				});

			var res = await msbCache.ShowWindowDialogAsync(MainWindow.Instsnce);
			if (res != Res.Yes)
				return;

			try
			{
				await _model.DeleteModAsync(SelectedMod);
				SelectedMod = null;
				RefreshAddedMods();
			}
			catch (Exception ex)
			{
				var msb = MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
				{
					WindowIcon = GetLogo(),
					ContentTitle = Res.ModRunnerModLauncherTitle,
					ContentMessage = ex.Message,
					ButtonDefinitions = ButtonEnum.Ok,
					Icon = Icon.Error,
					WindowStartupLocation = WindowStartupLocation.CenterOwner,
					MinWidth = 350
				});

				await msb.ShowWindowDialogAsync(MainWindow.Instsnce);
			}

		});
	}

	private async Task RenameMod()
	{
		if (SelectedMod == null)
			return;

		var renameRes = await ShowRenameDialog(SelectedMod.Name);
		if (!renameRes.OK)
			return;

		await _model.RenameMod(SelectedMod, renameRes.Text);
		RefreshAddedMods();
	}

	private async Task<TexInputBoxResult> ShowRenameDialog(string defaultModName)
	{
		bool IsValidText(string text)
		{
			var val = text.Trim();
			string pattern = @"[^\w\.\s@-]";
			var res = Regex.IsMatch(val, pattern);
			return !res;
		}

		TextInputBox textInputBox = new(
			Res.EnterModName, 
			defaultModName, 
			vm => vm.WhenAnyValue(x => x.Text, text => !string.IsNullOrWhiteSpace(text) && IsValidText(text)), Res.InvalidModName);

		return await textInputBox.ShowDialog(MainWindow.Instsnce);
	}

	private async Task<string> OpenFolderDialog()
	{
		var topLevel = TopLevel.GetTopLevel(MainWindow.Instsnce);

		if(topLevel != null)
		{
			var folder = (await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
			{
				Title = Res.SelectFolder,
				AllowMultiple = false
			})).FirstOrDefault();

			if(folder != null)
			{
				return folder.Path.LocalPath;
			}
		}

		return string.Empty;
	}

	private async Task<string> OpenFileDialog()
	{
		var topLevel = TopLevel.GetTopLevel(MainWindow.Instsnce);

		if (topLevel != null)
		{
			var file = (await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
			{
				Title = Res.SelectFile,
				AllowMultiple = false,
				FileTypeFilter = new List<FilePickerFileType> { new FilePickerFileType(null) {Patterns = _model.AvailableExts } }
			})).FirstOrDefault();

			if (file != null)
			{
				return file.Path.LocalPath;
			}
		}

		return string.Empty;
	}

	//https://github.com/dotnet/runtime/issues/17938
	private void OpenGitHubLink()
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			Process.Start(new ProcessStartInfo("cmd", $"/c start https://github.com/xEGOISTx/MudRunnerModLauncher"));
		}
	}

	private async void RefreshAddedMods()
	{
		string selectedModName = SelectedMod != null ? SelectedMod.Name : string.Empty;

		AddedMods = await _model.GetAddedModsAsync();

		if (string.IsNullOrEmpty(selectedModName))
			return;

		DirectoryInfo? selectedMod = AddedMods.FirstOrDefault(dir => dir.Name == selectedModName);
		if (selectedMod != null)
		{
			SelectedMod = selectedMod;
		}
	}

	private async Task BusyAction(Func<Task> action)
	{
		IsBusy = true;
		await action();
		IsBusy = false;
	}

	//пока так
	private WindowIcon GetLogo()
	{
		return new WindowIcon(AssetLoader.Open(new Uri("avares://MudRunnerModLauncher/Assets/logo.ico")));
	}
}


