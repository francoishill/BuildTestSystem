using System;
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
using Microsoft.WindowsAPICodePack.Taskbar;
using System.Windows.Interop;
using System.Windows.Threading;

namespace BuildTestSystem
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		public MainWindow()
		{
			InitializeComponent();
		}

		private void Window_Loaded(object sender, RoutedEventArgs e)
		{
			ShowIndeterminateProgress("Obtaining list");
			ThreadingInterop.PerformVoidFunctionSeperateThread(() =>
			{
				//Calls it to prevent delaying when obtaining list in future
				var apps = SettingsSimple.BuildTestSystemSettings.Instance.ListOfApplicationsToBuild;
				SettingsSimple.BuildTestSystemSettings.EnsureDefaultItemsInList();
				HideIndeterminateProgress(true);
			},
			false);
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
						TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.Error);
						buildApplication.LastBuildFeedback = "Newer version available: " + onlineVersionDetails.ApplicationVersion;
					}
					else//Unable to check for updates
					{
						TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.Error);
						buildApplication.LastBuildFeedback = "Error occurred checking for updates: " + errorIfNull;
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

					string changesText;
					if (TortoiseProcInterop.CheckFolderSubversionChanges(Path.GetDirectoryName(buildapp.SolutionFullpath), out changesText))
					{
						TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.Error);
						buildApplication.LastBuildFeedback = changesText;
					}
					else
						buildApplication.LastBuildResult = true;
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
				List<string> appswithErrors = new List<string>();
				for (int i = 0; i < items.Count; i++)
				{
					BuildApplication buildapp = items[i] as BuildApplication;
					buildapp.LastBuildFeedback = null;
					buildapp.HasFeedbackText = false;
					buildapp.LastBuildResult = null;
				}

				TaskbarManager.Instance.SetProgressValue(0, items.Count);
				TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.Normal);

				for (int i = 0; i < items.Count; i++)
				//Parallel.For(0, items.Count - 1, (i) =>
				{
					BuildApplication buildapp = items[i] as BuildApplication;
					ShowIndeterminateProgress("Building application: " + buildapp.ApplicationName, true);
					Stopwatch sw = Stopwatch.StartNew();
					List<string> csprojPaths;
					string err;
					bool buildSuccess = buildapp.PerformBuild(out csprojPaths, out err);
					Logging.LogInfoToFile(string.Format("Duration to build {0} was {1} seconds.", buildapp.ApplicationName, sw.Elapsed.TotalSeconds), Logging.ReportingFrequencies.Daily, "BuildTestSystem", "Benchmarks");
					if (!buildSuccess)
					{
						appswithErrors.Add(buildapp.ApplicationName);
						TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.Error);
					}
					TaskbarManager.Instance.SetProgressValue(i + 1, items.Count);
				}//);
				if (appswithErrors.Count > 0)
				{
					TaskbarManager.Instance.SetProgressValue(100, 100);
					//UserMessages.ShowErrorMessage("Error building the following apps: " + Environment.NewLine +
					//    string.Join(Environment.NewLine, appswithErrors));
				}
				else
					TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.NoProgress);
				HideIndeterminateProgress(true);
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

				TaskbarManager.Instance.SetProgressValue(0, items.Count);
				TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.Normal);

				int completedItemCount = 0;

				var buildApps = new List<BuildApplication>();
				foreach (var item in items)
					buildApps.Add(item as BuildApplication);
				buildApps.RemoveAll(ba => ba.IsInstalled == false);
				Parallel.ForEach<BuildApplication>(
					buildApps,
					(buildapp) =>
					{
						ShowIndeterminateProgress("Check for updates for : " + buildapp.ApplicationName, true);
						AppCheckForUpdates(buildapp, false);
						TaskbarManager.Instance.SetProgressValue(++completedItemCount, items.Count);
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
					TaskbarManager.Instance.SetProgressValue(100, 100);
					//UserMessages.ShowErrorMessage("Error building the following apps: " + Environment.NewLine +
					//    string.Join(Environment.NewLine, appswithErrors));
				}
				else
					TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.NoProgress);
				HideIndeterminateProgress(true);
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

				TaskbarManager.Instance.SetProgressValue(0, items.Count);
				TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.Normal);

				int completedItemCount = 0;

				var buildApps = new List<BuildApplication>();
				foreach (var item in items)
					buildApps.Add(item as BuildApplication);
				buildApps.RemoveAll(ba => ba.IsVersionControlled == false);
				Parallel.ForEach<BuildApplication>(
					buildApps,
					(buildapp) =>
					{
						ShowIndeterminateProgress("Check versioning status : " + buildapp.ApplicationName, true);
						AppCheckForSubversionChanges(buildapp, false);
						TaskbarManager.Instance.SetProgressValue(++completedItemCount, items.Count);
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
					TaskbarManager.Instance.SetProgressValue(100, 100);
					//UserMessages.ShowErrorMessage("Error building the following apps: " + Environment.NewLine +
					//    string.Join(Environment.NewLine, appswithErrors));
				}
				else
					TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.NoProgress);
				HideIndeterminateProgress(true);
				isbusy = false;
			},
			false);
		}

		private void ShowIndeterminateProgress(string message, bool fromSeparateThread = false)
		{
			Action act = delegate
			{
				statusLabel.Text = message;
				progressBarIndeterminate.Visibility = System.Windows.Visibility.Visible;
			};
			if (!fromSeparateThread)
				act();
			else
				this.Dispatcher.Invoke(act);
		}

		private void HideIndeterminateProgress(bool fromSeparateThread = false)
		{
			Action act = delegate
			{
				statusLabel.Text = null;
				progressBarIndeterminate.Visibility = System.Windows.Visibility.Hidden;
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
			if (IsBusyBuilding(true))
				return;
			isbusy = true;

			var buildapp = GetBuildApplicationFromMenuItem(sender);
			if (null == buildapp) return;

			ShowIndeterminateProgress("Building application " + buildapp.ApplicationName, false);
			ThreadingInterop.PerformOneArgFunctionSeperateThread<BuildApplication>((app) =>
			{
				List<string> csprojPaths;
				string err;
				app.PerformBuild(out csprojPaths, out err);
				HideIndeterminateProgress(true);
				isbusy = false;
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
			TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.Normal);
			TaskbarManager.Instance.SetProgressValue(0, 100);

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
					progperc => TaskbarManager.Instance.SetProgressValue(progperc, 100));
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

		public BuildApplication(string ApplicationName) : base(ApplicationName) { }

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
					string errorIfFail;
					this.PerformBuild(out csprojectPaths, out errorIfFail);
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
				new GradientStop(Color.FromArgb(100, 0, 130, 0), 0),
				new GradientStop(Color.FromArgb(140, 0, 180, 0), 0.7),
				new GradientStop(Color.FromArgb(100, 0, 130, 0), 1)
			});
		private static GradientStopCollection ErrorColorStops =
			new GradientStopCollection(new GradientStop[]
			{
				new GradientStop(Color.FromArgb(100, 130, 0, 0), 0),
				new GradientStop(Color.FromArgb(140, 180, 0, 0), 0.7),
				new GradientStop(Color.FromArgb(100, 130, 0, 0), 1)
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
}
