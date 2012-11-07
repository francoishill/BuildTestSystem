﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using SharedClasses;
using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using Microsoft.Build;
using Microsoft.Build.Execution;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework;
using System.Threading.Tasks;
using System.Windows.Shell;
using System.Windows.Interop;
using System.Windows.Threading;

namespace BuildTestSystem
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		private static TaskbarItemInfo WindowTaskBarItem;
		private static MainWindow windowInstance;

		public MainWindow()
		{
			InitializeComponent();

			WindowTaskBarItem = this.TaskbarItemInfo;
			windowInstance = this;

			this.TaskbarItemInfo.Overlay = (DrawingImage)this.Resources["overlayImageSucccess"];
		}

		public static void SetWindowProgressValue(double progressFractionOfOne)
		{
			windowInstance.Dispatcher.Invoke((Action<double>)(
				(progfact) =>
				{
					WindowTaskBarItem.ProgressValue = progfact;
				}),
				progressFractionOfOne);
		}

		public enum OverlayImage { Success, BuildFailed, NotUpToDate, VersionControlChanges };
		public static void SetWindowProgressState(TaskbarItemProgressState progressState, OverlayImage? overlayImage = null)
		{
			windowInstance.Dispatcher.Invoke((Action<TaskbarItemProgressState, OverlayImage>)(
				(state, image) =>
				{
					WindowTaskBarItem.ProgressState = state;

					WindowTaskBarItem.Overlay = null;
					if (image != null)
					{
						string resourceKeyForOverlay = "";
						switch (image)
						{
							case OverlayImage.Success:
								resourceKeyForOverlay = "overlayImageSucccess";
								break;
							case OverlayImage.BuildFailed:
								resourceKeyForOverlay = "overlayImageBuildFailed";
								break;
							case OverlayImage.NotUpToDate:
								resourceKeyForOverlay = "overlayImageNotUpToDate";
								break;
							case OverlayImage.VersionControlChanges:
								resourceKeyForOverlay = "overlayImageVersionControlChanges";
								break;
							default:
								break;
						}

						if (!string.IsNullOrWhiteSpace(resourceKeyForOverlay))
							WindowTaskBarItem.Overlay = (DrawingImage)windowInstance.Resources[resourceKeyForOverlay];
					}
				}),
				progressState,
				overlayImage);
		}

		private void Window_Loaded(object sender, RoutedEventArgs e)
		{
			this.WindowState = System.Windows.WindowState.Maximized;
			ShowIndeterminateProgress("Obtaining list");
			ThreadingInterop.PerformVoidFunctionSeperateThread(() =>
			{
				//Calls it to prevent delaying when obtaining list in future
				var apps = SettingsSimple.BuildTestSystemSettings.Instance.ListOfApplicationsToBuild;
				SettingsSimple.BuildTestSystemSettings.EnsureDefaultItemsInList();
				HideIndeterminateProgress(null, true);
			},
			false);
			ObtainApplicationList();
		}

		private void ForeachBuildapp(Action<BuildApplication> onBuildApp)
		{
			var items = tmpMainListbox.Items;
			for (int i = 0; i < items.Count; i++)
			{
				BuildApplication buildapp = items[i] as BuildApplication;
				if (buildapp != null)
					onBuildApp(buildapp);
			}
		}

		private void ForeachBuildappBorder(Action<BuildApplication, Border> onBuildappBorder)
		{
			ForeachBuildapp((ba) =>
			{
				var listboxItem = (ListBoxItem)tmpMainListbox.ItemContainerGenerator.ContainerFromItem(ba);
				ContentPresenter myContentPresenter = WPFHelper.GetVisualChild<ContentPresenter>(listboxItem);
				DataTemplate myDataTemplate = myContentPresenter.ContentTemplate;
				Border border = (Border)myDataTemplate.FindName("borderMainItemBorder", myContentPresenter);
				if (border != null)
					onBuildappBorder(ba, border);
			});
		}

		private void buttonObtainApplicationList_Click(object sender, RoutedEventArgs e)
		{
			ObtainApplicationList();
		}

		private void ObtainApplicationList()
		{
			radionButtonShowAll.IsChecked = true;
			tmpMainListbox.Items.Clear();
			var applicationlist = SettingsSimple.BuildTestSystemSettings.Instance.ListOfApplicationsToBuild;
			applicationlist.Sort(StringComparer.InvariantCultureIgnoreCase);
			foreach (var app in applicationlist)
			{
				//tmpMainListbox.Items.Add(app);
				tmpMainListbox.Items.Add(new BuildApplication(app));
			}
		}

		bool isbusy = false;
		private bool IsBusyBuilding(bool showErrorIfBusy = true)
		{
			if (isbusy)
				UserMessages.ShowWarningMessage("Cannot build, another build already in progress");
			return isbusy;
		}

		private static void AppCheckForUpdates(BuildApplication buildapp, bool separateThread = true)
		{
			Action<BuildApplication> checkForUpdatesAction =
				(buildApplication) =>
				{
					buildApplication.LastBuildFeedback = null;
					buildApplication.HasFeedbackText = false;
					buildApplication.LastBuildResult = null;

					string appExePath = PublishInterop.GetApplicationExePathFromApplicationName(buildApplication.ApplicationName);
					string InstalledVersion =
						File.Exists(appExePath)
						? FileVersionInfo.GetVersionInfo(appExePath).FileVersion
						: "0.0.0.0";
					string errorIfNull;
					SharedClasses.AutoUpdating.MockPublishDetails onlineVersionDetails;
					bool? checkSuccess =
						AutoUpdating.CheckForUpdatesSilently(buildApplication.ApplicationName, InstalledVersion, out errorIfNull, out onlineVersionDetails);
					if (checkSuccess == true)//Is up to date
					{
						buildApplication.LastBuildResult = true;
						return;
					}
					else if (checkSuccess == false)//Newer version available
					{
						MainWindow.SetWindowProgressState(TaskbarItemProgressState.Error);
						buildApplication.LastBuildFeedback = "Newer version available: " + onlineVersionDetails.ApplicationVersion;
					}
					else//Unable to check for updates
					{
						MainWindow.SetWindowProgressState(TaskbarItemProgressState.Error);
						buildApplication.LastBuildFeedback
							= /*"Error occurred checking for updates: " + */
							"ERROR: " + errorIfNull;
					}
				};

			if (separateThread)
				ThreadingInterop.PerformOneArgFunctionSeperateThread<BuildApplication>(checkForUpdatesAction, buildapp, true);
			else
				checkForUpdatesAction(buildapp);
		}

		private static void AppCheckForSubversionChanges(BuildApplication buildapp, bool separateThread = true)
		{
			Action<BuildApplication> checkForSubversionChanges =
				(buildApplication) =>
				{
					buildApplication.LastBuildFeedback = null;
					buildApplication.HasFeedbackText = false;
					buildApplication.LastBuildResult = null;
					buildapp.CurrentProgressPercentage = null;

					string changesText;
					if (TortoiseProcInterop.CheckFolderSubversionChanges(Path.GetDirectoryName(buildapp.SolutionFullpath), out changesText))
					{
						MainWindow.SetWindowProgressState(TaskbarItemProgressState.Error);
						buildApplication.LastBuildFeedback = changesText;
					}
					else
						buildApplication.LastBuildResult = true;
					buildapp.CurrentProgressPercentage = 0;
				};

			if (separateThread)
				ThreadingInterop.PerformOneArgFunctionSeperateThread<BuildApplication>(checkForSubversionChanges, buildapp, true);
			else
				checkForSubversionChanges(buildapp);
		}

		private void buttonBuildAll_Click(object sender, RoutedEventArgs e)
		{
			if (IsBusyBuilding(true))
				return;
			isbusy = true;

			ShowIndeterminateProgress("Starting to build applications, please wait...");
			ThreadingInterop.PerformVoidFunctionSeperateThread(() =>
			{
				var items = tmpMainListbox.Items;
				List<BuildApplication> applist = new List<BuildApplication>();
				//List<string> appswithErrors = new List<string>();
				for (int i = 0; i < items.Count; i++)
				{
					BuildApplication buildapp = items[i] as BuildApplication;
					buildapp.LastBuildFeedback = null;
					buildapp.HasFeedbackText = false;
					buildapp.LastBuildResult = null;
					applist.Add(buildapp);
				}

				MainWindow.SetWindowProgressValue(0);
				MainWindow.SetWindowProgressState(TaskbarItemProgressState.Normal);

				Dictionary<VSBuildProject, string> errors;
				Dictionary<BuildApplication, Stopwatch> stopwatches = new Dictionary<BuildApplication, Stopwatch>();
				int completeCount = 0;
				var allBuildResult = VSBuildProject.PerformMultipleBuild(
					applist,
					out errors,
					(appwhichstart) =>
					{
						ShowIndeterminateProgress("Building application: " + appwhichstart.ApplicationName, (BuildApplication)appwhichstart, true);
						stopwatches.Add((BuildApplication)appwhichstart, Stopwatch.StartNew());
					},
					(appwhichbuildcomplete, buildSuccess) =>
					{
						Stopwatch sw = stopwatches[(BuildApplication)appwhichbuildcomplete];
						Logging.LogInfoToFile(string.Format("Duration to build {0} was {1} seconds.", appwhichbuildcomplete.ApplicationName, sw.Elapsed.TotalSeconds), Logging.ReportingFrequencies.Daily, "BuildTestSystem", "Benchmarks");
						if (!buildSuccess)
						{
							//appswithErrors.Add(buildapp.ApplicationName);
							MainWindow.SetWindowProgressState(TaskbarItemProgressState.Error);
						}
						HideIndeterminateProgress((BuildApplication)appwhichbuildcomplete, true);
						MainWindow.SetWindowProgressValue(((double)(++completeCount)) / (double)items.Count);
					});
				var appswithErrors = allBuildResult.Keys.Where(k => !allBuildResult[k]).Select(k => k.ApplicationName).ToList();
				//for (int i = 0; i < items.Count; i++)
				/*Parallel.For(0, items.Count - 1, (i) =>
				{
					BuildApplication buildapp = items[i] as BuildApplication;
					ShowIndeterminateProgress("Building application: " + buildapp.ApplicationName, buildapp, true);
					Stopwatch sw = Stopwatch.StartNew();
					List<string> csprojPaths;
					string err;
					bool buildSuccess = buildapp.PerformBuild(out csprojPaths, out err);
					Logging.LogInfoToFile(string.Format("Duration to build {0} was {1} seconds.", buildapp.ApplicationName, sw.Elapsed.TotalSeconds), Logging.ReportingFrequencies.Daily, "BuildTestSystem", "Benchmarks");
					if (!buildSuccess)
					{
						appswithErrors.Add(buildapp.ApplicationName);
						MainWindow.SetWindowProgressState(TaskbarItemProgressState.Error);
					}
					HideIndeterminateProgress(buildapp, true);
					MainWindow.SetWindowProgressValue(((double)(i + 1)) / (double)items.Count);
				});*/
				if (appswithErrors.Count > 0)
				{
					MainWindow.SetWindowProgressValue(1);
					MainWindow.SetWindowProgressState(TaskbarItemProgressState.Error, OverlayImage.BuildFailed);
					//UserMessages.ShowErrorMessage("Error building the following apps: " + Environment.NewLine +
					//    string.Join(Environment.NewLine, appswithErrors));
				}
				else
					MainWindow.SetWindowProgressState(TaskbarItemProgressState.None);
				HideIndeterminateProgress(null, true);
				isbusy = false;
			},
			false);
		}

		private void buttonCheckForUpdatesAll_Click(object sender, RoutedEventArgs e)
		{
			if (IsBusyBuilding(true))
				return;
			isbusy = true;

			ShowIndeterminateProgress("Starting to check applications for updates, please wait...");
			ThreadingInterop.PerformVoidFunctionSeperateThread(() =>
			{
				var items = tmpMainListbox.Items;
				List<string> appswithErrors = new List<string>();
				for (int i = 0; i < items.Count; i++)
				{
					BuildApplication buildapp = items[i] as BuildApplication;
					buildapp.LastBuildFeedback = null;
					buildapp.HasFeedbackText = false;
					buildapp.LastBuildResult = null;
				}

				MainWindow.SetWindowProgressValue(0);
				MainWindow.SetWindowProgressState(TaskbarItemProgressState.Normal);

				int completedItemCount = 0;

				var buildApps = new List<BuildApplication>();
				foreach (var item in items)
					buildApps.Add(item as BuildApplication);
				buildApps.RemoveAll(ba => ba.IsInstalled == false);
				Parallel.ForEach<BuildApplication>(
					buildApps,
					(buildapp) =>
					{
						ShowIndeterminateProgress("Check for updates for : " + buildapp.ApplicationName, buildapp, true);
						AppCheckForUpdates(buildapp, false);
						if (!string.IsNullOrWhiteSpace(buildapp.LastBuildFeedback))
							appswithErrors.Add(buildapp.ApplicationName);
						HideIndeterminateProgress(buildapp, true);
						MainWindow.SetWindowProgressValue((double)++completedItemCount / (double)items.Count);
					});

				//for (int i = 0; i < items.Count; i++)
				////Parallel.For(0, items.Count - 1, (i) =>
				//{
				//    BuildApplication buildapp = items[i] as BuildApplication;
				//    ShowIndeterminateProgress("Building application: " + buildapp.ApplicationName, true);
				//    AppCheckForUpdates(buildapp, false);
				//    TaskbarManager.Instance.SetProgressValue(i + 1, items.Count);
				//}//);
				if (appswithErrors.Count > 0)
				{
					MainWindow.SetWindowProgressValue(1);
					MainWindow.SetWindowProgressState(TaskbarItemProgressState.Error, OverlayImage.NotUpToDate);
					//UserMessages.ShowErrorMessage("Error building the following apps: " + Environment.NewLine +
					//    string.Join(Environment.NewLine, appswithErrors));
				}
				else
					MainWindow.SetWindowProgressState(TaskbarItemProgressState.None);
				HideIndeterminateProgress(null, true);
				isbusy = false;
			},
			false);
		}

		private void buttonCheckVersioningStatusAll_Click(object sender, RoutedEventArgs e)
		{
			if (IsBusyBuilding(true))
				return;
			isbusy = true;

			ShowIndeterminateProgress("Starting to check version control statusses, please wait...");
			ThreadingInterop.PerformVoidFunctionSeperateThread(() =>
			{
				var items = tmpMainListbox.Items;
				List<string> appswithErrors = new List<string>();
				for (int i = 0; i < items.Count; i++)
				{
					BuildApplication buildapp = items[i] as BuildApplication;
					buildapp.LastBuildFeedback = null;
					buildapp.HasFeedbackText = false;
					buildapp.LastBuildResult = null;
				}

				MainWindow.SetWindowProgressValue(0);
				MainWindow.SetWindowProgressState(TaskbarItemProgressState.Normal);

				int completedItemCount = 0;

				var buildApps = new List<BuildApplication>();
				foreach (var item in items)
					buildApps.Add(item as BuildApplication);
				buildApps.RemoveAll(ba => ba.IsVersionControlled == false);
				Parallel.ForEach<BuildApplication>(
					buildApps,
					(buildapp) =>
					{
						ShowIndeterminateProgress("Check versioning status : " + buildapp.ApplicationName, buildapp, true);
						AppCheckForSubversionChanges(buildapp, false);
						if (!string.IsNullOrWhiteSpace(buildapp.LastBuildFeedback))
							appswithErrors.Add(buildapp.ApplicationName);
						HideIndeterminateProgress(buildapp, true);
						MainWindow.SetWindowProgressValue((double)++completedItemCount / (double)items.Count);
					});

				//for (int i = 0; i < items.Count; i++)
				////Parallel.For(0, items.Count - 1, (i) =>
				//{
				//    BuildApplication buildapp = items[i] as BuildApplication;
				//    ShowIndeterminateProgress("Building application: " + buildapp.ApplicationName, true);
				//    AppCheckForUpdates(buildapp, false);
				//    TaskbarManager.Instance.SetProgressValue(i + 1, items.Count);
				//}//);
				if (appswithErrors.Count > 0)
				{
					MainWindow.SetWindowProgressValue(1);
					MainWindow.SetWindowProgressState(TaskbarItemProgressState.Error, OverlayImage.VersionControlChanges);
					//UserMessages.ShowErrorMessage("Error building the following apps: " + Environment.NewLine +
					//    string.Join(Environment.NewLine, appswithErrors));
				}
				else
					MainWindow.SetWindowProgressState(TaskbarItemProgressState.None);
				HideIndeterminateProgress(null, true);
				isbusy = false;
			},
			false);
		}

		private void ShowIndeterminateProgress(string message, BuildApplication buildappToSetProgress = null, bool fromSeparateThread = false)
		{
			Action act = delegate
			{
				statusLabel.Text = message;
				progressBarIndeterminate.Visibility = System.Windows.Visibility.Visible;
				if (buildappToSetProgress != null)
					buildappToSetProgress.CurrentProgressPercentage = null;
			};
			if (!fromSeparateThread)
				act();
			else
				this.Dispatcher.Invoke(act);
		}

		private void HideIndeterminateProgress(BuildApplication buildappToSetProgress = null, bool fromSeparateThread = false)
		{
			Action act = delegate
			{
				if (buildappToSetProgress == null)
				{
					statusLabel.Text = null;
					progressBarIndeterminate.Visibility = System.Windows.Visibility.Hidden;
				}
				else
					buildappToSetProgress.CurrentProgressPercentage = 0;
			};
			if (!fromSeparateThread)
				act();
			else
				this.Dispatcher.Invoke(act);
		}

		private BuildApplication GetBuildApplicationFromMenuItem(object potentialmenuitem)
		{
			var menuitem = potentialmenuitem as MenuItem;
			if (null == menuitem) return null;
			var buildapp = menuitem.DataContext as BuildApplication;
			if (null == buildapp) return null;
			return buildapp;
		}

		private void contextmenuitemRebuildThisApplication(object sender, RoutedEventArgs e)
		{
			//if (IsBusyBuilding(true))
			//    return;
			//isbusy = true;

			var buildapp = GetBuildApplicationFromMenuItem(sender);
			if (null == buildapp) return;

			ShowIndeterminateProgress("Building application " + buildapp.ApplicationName, buildapp, false);
			ThreadingInterop.PerformOneArgFunctionSeperateThread<BuildApplication>((app) =>
			{
				List<string> csprojPaths;
				app.PerformBuild(null, out csprojPaths);
				HideIndeterminateProgress(null, true);
				HideIndeterminateProgress(app, true);
				//isbusy = false;
			},
			buildapp,
			false);
		}

		private void contextmenuOpenWithCSharpExpress(object sender, RoutedEventArgs e)
		{
			var buildapp = GetBuildApplicationFromMenuItem(sender);
			if (null == buildapp) return;
			buildapp.OpenInCSharpExpress();
		}

		private void contextmenuPublishOnline(object sender, RoutedEventArgs e)
		{
			var buildapplication = GetBuildApplicationFromMenuItem(sender);
			if (null == buildapplication) return;
			MainWindow.SetWindowProgressState(TaskbarItemProgressState.Normal);
			MainWindow.SetWindowProgressValue(0);

			buildapplication.LastBuildFeedback = "";
			ThreadingInterop.PerformOneArgFunctionSeperateThread<BuildApplication>((buildapp) =>
			{
				bool publishResult = buildapp.PerformPublishOnline(
					(mess, messagetype) =>
					{
						switch (messagetype)
						{
							case FeedbackMessageTypes.Success: buildapp.AppendLastBuildFeedback(mess); break;
							case FeedbackMessageTypes.Error: UserMessages.ShowErrorMessage(mess); break;
							case FeedbackMessageTypes.Warning: UserMessages.ShowWarningMessage(mess); break;
							case FeedbackMessageTypes.Status: buildapp.AppendLastBuildFeedback(mess); break;
							default: UserMessages.ShowWarningMessage("Cannot use messagetype = " + messagetype.ToString()); break;
						}
					},
					progperc => MainWindow.SetWindowProgressValue((double)progperc / 100D));
			},
			buildapplication,
			false);
		}

		private void contextmenuCheckForUpdates(object sender, RoutedEventArgs e)
		{
			var buildapplication = GetBuildApplicationFromMenuItem(sender);
			if (null == buildapplication) return;
			AppCheckForUpdates(buildapplication, true);
		}

		private void contextmenuInstallLatestVersion(object sender, RoutedEventArgs e)
		{
			var buildapplication = GetBuildApplicationFromMenuItem(sender);
			if (null == buildapplication) return;
			AutoUpdating.InstallLatest(buildapplication.ApplicationName,
				err => UserMessages.ShowErrorMessage(err));
		}

		private void contextmenuCheckSubversionChanges(object sender, RoutedEventArgs e)
		{
			var buildapp = GetBuildApplicationFromMenuItem(sender);
			if (null == buildapp) return;
			if (buildapp.IsVersionControlled != true)
				UserMessages.ShowWarningMessage("Directory is not version controlled: " + buildapp.GetSolutionDirectory());
			else
				AppCheckForSubversionChanges(buildapp, true);
		}

		private void contextmenuSubversionUpdate(object sender, RoutedEventArgs e)
		{
			var buildapp = GetBuildApplicationFromMenuItem(sender);
			if (null == buildapp) return;
			if (buildapp.IsVersionControlled != true)
				UserMessages.ShowWarningMessage("Directory is not version controlled: " + buildapp.GetSolutionDirectory());
			else
			{
				ThreadingInterop.PerformOneArgFunctionSeperateThread<BuildApplication>(
					(b) =>
					{
						Process p = TortoiseProcInterop.StartTortoiseProc(TortoiseProcInterop.TortoiseCommands.Update, buildapp.GetSolutionDirectory());
						p.WaitForExit();
						AppCheckForSubversionChanges(b);
					},
					buildapp,
					false);
			}
		}

		private void contextmenuShowSubversionLog(object sender, RoutedEventArgs e)
		{
			var buildapp = GetBuildApplicationFromMenuItem(sender);
			if (null == buildapp) return;
			if (buildapp.IsVersionControlled != true)
				UserMessages.ShowWarningMessage("Directory is not version controlled: " + buildapp.GetSolutionDirectory());
			else
			{
				ThreadingInterop.PerformOneArgFunctionSeperateThread<BuildApplication>(
					(b) =>
					{
						Process p = TortoiseProcInterop.StartTortoiseProc(TortoiseProcInterop.TortoiseCommands.Log, buildapp.GetSolutionDirectory());
						p.WaitForExit();
						AppCheckForSubversionChanges(b);
					},
					buildapp,
					false);
			}
		}

		private void contextmenuSubversionCommitChanges(object sender, RoutedEventArgs e)
		{
			var buildapp = GetBuildApplicationFromMenuItem(sender);
			if (null == buildapp) return;
			if (buildapp.IsVersionControlled != true)
				UserMessages.ShowWarningMessage("Directory is not version controlled: " + buildapp.GetSolutionDirectory());
			else
			{
				ThreadingInterop.PerformOneArgFunctionSeperateThread<BuildApplication>(
					(b) =>
					{
						Process p = TortoiseProcInterop.StartTortoiseProc(TortoiseProcInterop.TortoiseCommands.Commit, buildapp.GetSolutionDirectory());
						p.WaitForExit();
						AppCheckForSubversionChanges(b);
					},
					buildapp,
					false);
			}
		}

		private void textblockAbout_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
		{
			AboutWindow2.ShowAboutWindow(new System.Collections.ObjectModel.ObservableCollection<DisplayItem>()
			{
				new DisplayItem("Author", "Francois Hill"),
				new DisplayItem("Icon obtained from", "http://www.icons-land.com", "http://www.icons-land.com/vista-base-software-icons.php")

			});
		}

		private void radioButtonShowAll_Click(object sender, RoutedEventArgs e)
		{
			ForeachBuildappBorder((ba, border) =>
			{
				border.Visibility = System.Windows.Visibility.Visible;
			});
		}

		private void radioButtonShowInstalled_Click(object sender, RoutedEventArgs e)
		{
			ForeachBuildappBorder((ba, border) =>
			{
				if (ba.IsInstalled == true)
					border.Visibility = System.Windows.Visibility.Visible;
				else
					border.Visibility = System.Windows.Visibility.Collapsed;
			});
		}

		private void radioButtonShowUninstalled_Click(object sender, RoutedEventArgs e)
		{
			ForeachBuildappBorder((ba, border) =>
			{
				if (!ba.IsInstalled == true)
					border.Visibility = System.Windows.Visibility.Visible;
				else
					border.Visibility = System.Windows.Visibility.Collapsed;
			});
		}

		private void radioButtonShowVersioncontrolled_Click(object sender, RoutedEventArgs e)
		{
			ForeachBuildappBorder((ba, border) =>
			{
				if (ba.IsVersionControlled == true)
					border.Visibility = System.Windows.Visibility.Visible;
				else
					border.Visibility = System.Windows.Visibility.Collapsed;
			});
		}

		private void radioButtonShowUnversioncontrolled_Click(object sender, RoutedEventArgs e)
		{
			ForeachBuildappBorder((ba, border) =>
			{
				if (!ba.IsVersionControlled == true)
					border.Visibility = System.Windows.Visibility.Visible;
				else
					border.Visibility = System.Windows.Visibility.Collapsed;
			});
		}
	}

	public class BuildApplication : VSBuildProject, INotifyPropertyChanged
	{
		private string _applicationname;
		public override string ApplicationName { get { return _applicationname; } set { _applicationname = value; OnPropertyChanged("ApplicationName"); } }
		private string _lastbuildfeedback;
		public override string LastBuildFeedback { get { return _lastbuildfeedback ?? ""; } set { _lastbuildfeedback = value; HasFeedbackText = value != null; OnPropertyChanged("LastBuildFeedback"); } }
		private bool _hasfeedbacktext;
		public override bool HasFeedbackText { get { return _hasfeedbacktext; } set { _hasfeedbacktext = value; LastBuildResult = !value; OnPropertyChanged("HasFeedbackText"); } }
		private bool? _lastbuildresult;
		public override bool? LastBuildResult { get { return _lastbuildresult; } set { _lastbuildresult = value; OnPropertyChanged("LastBuildResult"); } }
		public bool? IsInstalled { get { return PublishInterop.IsInstalled(this.ApplicationName); } }
		public bool? IsVersionControlled { get { return DirIsValidSvnPath(Path.GetDirectoryName(this.SolutionFullpath)); } }

		private int? _currentprogressPprcentage;
		public int? CurrentProgressPercentage { get { return _currentprogressPprcentage; } set { _currentprogressPprcentage = value; OnPropertyChanged("CurrentProgressPercentage"); } }

		public BuildApplication(string ApplicationName, Action<string> actionOnError = null)
			: base(ApplicationName, null, actionOnError)
		{ CurrentProgressPercentage = 0; }

		public event PropertyChangedEventHandler PropertyChanged = new PropertyChangedEventHandler(delegate { });
		public void OnPropertyChanged(string propertyName) { PropertyChanged(this, new PropertyChangedEventArgs(propertyName)); }

		private bool DirIsValidSvnPath(string dir)
		{
			if (!Directory.Exists(dir))
				return false;
			return Directory.Exists(System.IO.Path.Combine(dir, ".svn"));
		}

		public void OpenInCSharpExpress()
		{
			var csharpPath = RegistryInterop.GetAppPathFromRegistry("VCSExpress.exe");
			if (csharpPath == null)
			{
				UserMessages.ShowErrorMessage("Cannot obtain CSharp Express path from registry.");
				return;
			}
			ThreadingInterop.PerformOneArgFunctionSeperateThread<string>((cspath) =>
			{
				var proc = Process.Start(csharpPath, "\"" + this.SolutionFullpath + "\"");
				if (proc != null)
				{
					proc.WaitForExit();
					List<string> csprojectPaths;
					this.PerformBuild(null, out csprojectPaths);
				}
			},
			csharpPath,
			false);
		}

		public void AppendLastBuildFeedback(string textToAppend)
		{
			if (!string.IsNullOrWhiteSpace(this.LastBuildFeedback))
				this.LastBuildFeedback += Environment.NewLine;
			this.LastBuildFeedback += textToAppend;
		}
	}

	public class BoolToBrushConverter : IValueConverter
	{
		private static GradientStopCollection SuccessColorStops =
			new GradientStopCollection(new GradientStop[]
 			{
				new GradientStop(Color.FromArgb(20, 0, 130, 0), 0),
				new GradientStop(Color.FromArgb(40, 0, 180, 0), 0.7),
				new GradientStop(Color.FromArgb(20, 0, 130, 0), 1)
			});
		private static GradientStopCollection ErrorColorStops =
			new GradientStopCollection(new GradientStop[]
			{
				new GradientStop(Color.FromArgb(20, 130, 0, 0), 0),
				new GradientStop(Color.FromArgb(40, 180, 0, 0), 0.7),
				new GradientStop(Color.FromArgb(20, 130, 0, 0), 1)
			});

		public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			if (!(value is bool?) && value != null)//If null we assume its a null bool?
				return Brushes.Yellow;

			bool? boolval = (bool?)value;

			if (!boolval.HasValue)
				return Brushes.Transparent;
			else if (true == boolval.Value)
				return new LinearGradientBrush(SuccessColorStops, new Point(0, 0), new Point(0, 1));
			else
				return new LinearGradientBrush(ErrorColorStops, new Point(0, 0), new Point(0, 1));

		}

		public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}

	public class BoolToOpacityConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			if (!(value is bool?) && value != null)//If null we assume its a null bool?
				return 0.05;

			bool? boolval = (bool?)value;
			if (!boolval.HasValue)
				return 0.05;
			else if (boolval.Value)
				return 1;
			else
				return 0.15;
		}

		public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}

	public class NullableIntToIntConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			if (value == null || !(value is int?))
				return 0;

			return (value as int?).Value;
		}

		public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}

	public class NullableIntToBooleanConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			bool paramIsOpposite = parameter != null && parameter.ToString().Equals("opposite", StringComparison.InvariantCultureIgnoreCase);
			if (value == null || !(value is int?))
			{
				if (paramIsOpposite)
					return true;
				else
					return false;
			}

			if (paramIsOpposite)
				return false;
			else
				return true;
		}

		public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}

	public class NullableIntToVisibilityConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			bool hideInsteadOfCollapse = parameter != null && parameter.ToString().Equals("HideInsteadOfCollapse", StringComparison.InvariantCultureIgnoreCase);
			if ((value is int) && (int)value == 0)
				return hideInsteadOfCollapse ? Visibility.Hidden : Visibility.Collapsed;
			else
				return Visibility.Visible;
		}

		public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}
}
