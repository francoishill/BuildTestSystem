using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using SharedClasses;

namespace BuildTestSystem
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		private static TaskbarItemInfo _windowTaskBarItem;
		private static MainWindow _windowInstance;

		private ObservableCollection<BuildApplication> listOfApplications = new ObservableCollection<BuildApplication>();

		public MainWindow()
		{
			InitializeComponent();

			_windowTaskBarItem = this.TaskbarItemInfo;
			_windowInstance = this;

			this.TaskbarItemInfo.Overlay = (DrawingImage)this.Resources["overlayImageSucccess"];

			tmpMainTreeview.ItemsSource = listOfApplications;
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
				ObtainApplicationList();
			},
			false);
		}

		public static void SetWindowProgressValue(double progressFractionOfOne)
		{
			_windowInstance.Dispatcher.Invoke((Action<double>)(
				(progfact) =>
				{
					_windowTaskBarItem.ProgressValue = progfact;
				}),
				progressFractionOfOne);
		}

		public enum OverlayImage { Success, BuildFailed, NotUpToDate, VersionControlChanges };
		public static void SetWindowProgressState(TaskbarItemProgressState progressState, OverlayImage? overlayImage = null)
		{
			_windowInstance.Dispatcher.Invoke((Action<TaskbarItemProgressState, OverlayImage?>)(
				(state, image) =>
				{
					_windowTaskBarItem.ProgressState = state;

					_windowTaskBarItem.Overlay = null;
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
							_windowTaskBarItem.Overlay = (DrawingImage)_windowInstance.Resources[resourceKeyForOverlay];
					}
				}),
				progressState,
				overlayImage);
		}

		private void ForeachBuildapp(Action<BuildApplication> onBuildApp)
		{
			var items = tmpMainTreeview.Items;
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
				var treeviewItem = (TreeViewItem)tmpMainTreeview.ItemContainerGenerator.ContainerFromItem(ba);
				ContentPresenter myContentPresenter = treeviewItem.FindVisualChild<ContentPresenter>();
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
			Action act = delegate
			{
				radionButtonShowAll.IsChecked = true;
				//tmpMainTreeview.Items.Clear();
				var applist = SettingsSimple.BuildTestSystemSettings.Instance.ListOfApplicationsToBuild;
				applist.Sort(StringComparer.InvariantCultureIgnoreCase);
				foreach (var app in applist)
				{
					var newApp = new BuildApplication(app, err => FeedbackMessageAction(null, err, FeedbackMessageTypes.Error));
					//tmpMainListbox.Items.Add(newApp);
					//tmpMainTreeview.Items.Add(new BuildApplication(newApp));
					newApp.PropertyChanged += (sn, pn) =>
					{
						if (pn.PropertyName.Equals("IsSelected", StringComparison.InvariantCultureIgnoreCase))
						{
							UpdateControlsAffectedBySelection();
						}
					};
					listOfApplications.Add(newApp);
				}
				UpdateControlsAffectedBySelection();
			};

			if (Thread.CurrentThread != this.Dispatcher.Thread)
				this.Dispatcher.Invoke(act);
			else
				act();
		}

		private void UpdateControlsAffectedBySelection()
		{
			int selectedCount = listOfApplications.Count(a => a.IsSelected == true);
			int unselectedCount = listOfApplications.Count(a => a.IsSelected == false);
			buttonUnselectAll.Visibility =
				selectedCount > 0
				? Visibility.Visible
				: Visibility.Hidden;
			buttonSelectAll.Visibility =
				unselectedCount > 0
				? Visibility.Visible
				: Visibility.Hidden;
			textblockSelectedCount.Text = string.Format("{0}/{1} selected", selectedCount, listOfApplications.Count);
		}

		private static void AppCheckForUpdates(BuildApplication buildapp, bool separateThread = true)
		{
			Action<BuildApplication> checkForUpdatesAction =
                (buildApplication) =>
				{
					buildApplication.ClearCurrentStatusText();

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
						buildApplication.CurrentStatus = BuildApplication.StatusTypes.Success;
						return;
					}
					else if (checkSuccess == false)//Newer version available
					{
						MainWindow.SetWindowProgressState(TaskbarItemProgressState.Error);
						buildApplication.CurrentStatusText = "Newer version available: " + onlineVersionDetails.ApplicationVersion;
					}
					else//Unable to check for updates
					{
						MainWindow.SetWindowProgressState(TaskbarItemProgressState.Error);
						buildApplication.CurrentStatusText
							= /*"Error occurred checking for updates: " + */
							"ERROR: " + errorIfNull;
					}
				};

			if (separateThread)
				ThreadingInterop.PerformOneArgFunctionSeperateThread<BuildApplication>(checkForUpdatesAction, buildapp, false);
			else
				checkForUpdatesAction(buildapp);
		}

		private static void AppCheckForSubversionChanges(BuildApplication buildapp, bool separateThread = true)
		{
			Action<BuildApplication> checkForSubversionChanges =
                (buildApplication) =>
				{
					buildApplication.ClearCurrentStatusText();

					string changesText;
					if (TortoiseProcInterop.CheckFolderSubversionChanges(Path.GetDirectoryName(buildapp.SolutionFullpath), out changesText))
					{
						MainWindow.SetWindowProgressState(TaskbarItemProgressState.Error);
						buildApplication.CurrentStatus = BuildApplication.StatusTypes.Warning;
						buildApplication.CurrentStatusText = changesText;
					}
					else
						buildApplication.CurrentStatus = BuildApplication.StatusTypes.Success;
					buildapp.CurrentProgressPercentage = 0;
				};

			if (separateThread)
				ThreadingInterop.PerformOneArgFunctionSeperateThread<BuildApplication>(checkForSubversionChanges, buildapp, true);
			else
				checkForSubversionChanges(buildapp);
		}

		private void buttonBuildAll_Click(object sender, RoutedEventArgs e)
		{
			BuildListOfApplications(listOfApplications);
		}

		private void BuildListOfApplications(IEnumerable<BuildApplication> listOfAppsToBuild)
		{
			if (BuildApplication.IsBusyBuilding(true))
				return;
			BuildApplication.SetIsBusyBuildingTrue(true);

			ShowIndeterminateProgress("Starting to build applications, please wait...");

			//var items = tmpMainTreeview.Items;
			//List<BuildApplication> applist = new List<BuildApplication>();
			////List<string> appswithErrors = new List<string>();
			//for (int i = 0; i < items.Count; i++)
			//{
			//    BuildApplication buildapp = items[i] as BuildApplication;
			//    buildapp.CurrentStatusText = null;
			//    buildapp.LastBuildResult = null;
			//    applist.Add(buildapp);
			//}

			ThreadingInterop.PerformOneArgFunctionSeperateThread<IEnumerable<BuildApplication>>((applist) =>
			{
				MainWindow.SetWindowProgressValue(0);
				MainWindow.SetWindowProgressState(TaskbarItemProgressState.Normal);

				Dictionary<VsBuildProject, string> errors;
				var stopwatches = new Dictionary<BuildApplication, Stopwatch>();
				var completeCount = 0;
				var allBuildResult = VsBuildProject.PerformMultipleBuild(
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
						MainWindow.SetWindowProgressValue(((double)(++completeCount)) / (double)listOfAppsToBuild.Count());
					});
				var appswithErrors = allBuildResult.Keys.Where(k => !allBuildResult[k]).Select(k => k.ApplicationName).ToList();

				foreach (var app in applist)
					if (appswithErrors.Count(a => a.Equals(app.ApplicationName, StringComparison.InvariantCultureIgnoreCase)) == 0)
						app.CurrentStatus = BuildApplication.StatusTypes.Success;
					else
						app.CurrentStatus = BuildApplication.StatusTypes.Error;

				if (appswithErrors.Count > 0)
				{
					MainWindow.SetWindowProgressValue(1);
					MainWindow.SetWindowProgressState(TaskbarItemProgressState.Error, OverlayImage.BuildFailed);
					//UserMessages.ShowErrorMessage("Error building the following apps: " + Environment.NewLine +
					//    string.Join(Environment.NewLine, appswithErrors));

					//foreach (var appWithError
				}
				else
				{
					MainWindow.SetWindowProgressState(TaskbarItemProgressState.None);
				}
				HideIndeterminateProgress(null, true);
				BuildApplication.SetIsBusyBuildingTrue(false);
			},
			listOfAppsToBuild,
			false);
		}

		private void buttonCheckForUpdatesAll_Click(object sender, RoutedEventArgs e)
		{
			if (BuildApplication.IsBusyBuilding(true))
				return;
			BuildApplication.SetIsBusyBuildingTrue(true);

			ShowIndeterminateProgress("Starting to check applications for updates, please wait...");
			ThreadingInterop.PerformVoidFunctionSeperateThread(() =>
			{
				var items = tmpMainTreeview.Items;
				List<string> appswithErrors = new List<string>();
				for (int i = 0; i < items.Count; i++)
					(items[i] as BuildApplication)
						.ClearCurrentStatusText();

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
						if (!string.IsNullOrWhiteSpace(buildapp.CurrentStatusText))
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
				BuildApplication.SetIsBusyBuildingTrue(false);
			},
			false);
		}

		private void buttonCheckVersioningStatusAll_Click(object sender, RoutedEventArgs e)
		{
			if (BuildApplication.IsBusyBuilding(true))
				return;
			BuildApplication.SetIsBusyBuildingTrue(true);

			ShowIndeterminateProgress("Starting to check version control statusses, please wait...");
			ThreadingInterop.PerformVoidFunctionSeperateThread(() =>
			{
				var items = tmpMainTreeview.Items;
				List<string> appswithErrors = new List<string>();
				for (int i = 0; i < items.Count; i++)
					(items[i] as BuildApplication)
						.ClearCurrentStatusText();

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
						if (!string.IsNullOrWhiteSpace(buildapp.CurrentStatusText))
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
				BuildApplication.SetIsBusyBuildingTrue(false);
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

		private BuildApplication GetBuildApplicationFromFrameworkElement(object potentialFrameworkelement)
		{
			var f = potentialFrameworkelement as FrameworkElement;
			if (null == f) return null;
			var buildapp = f.DataContext as BuildApplication;
			if (null == buildapp) return null;
			return buildapp;
		}

		private BuildApplication GetBuildApplicationFromApplicationName(string appname)
		{
			foreach (BuildApplication ba in tmpMainTreeview.Items)
				if (ba.ApplicationName.Equals(appname, StringComparison.InvariantCultureIgnoreCase))
					return ba;
			return null;
		}

		private List<BuildApplication> GetBuildAppList_FromContextMenu(object sender, bool returnOnlyOneItem = false)
		{
			List<BuildApplication> tmplist = new List<BuildApplication>();
			ForeachBuildapp(
				(ba) =>
				{
					if (ba.IsSelected == true)
						tmplist.Add(ba);
				});
			//if (tmplist.Count == 0)//There were no selected items
			//{
			//Just always include current item
			var buildapp = GetBuildApplicationFromFrameworkElement(sender);
			if (buildapp != null)
				if (!tmplist.Contains(buildapp))
					tmplist.Add(buildapp);
			//}
			if (tmplist.Count == 0)
				UserMessages.ShowWarningMessage("Warning could not GetBuildAppList_FromContextMenu");

			if (returnOnlyOneItem)
			{
				if (buildapp != null && tmplist.Count > 1)
				{
					tmplist.RemoveAll(b => b != buildapp);
					UserMessages.ShowWarningMessage("At this stage, some items (running on separate threads), like Rebuild, will not work with multiple selected items, removing all except the current item");
				}
			}

			return tmplist;
		}

		private void contextmenuitemRebuildThisApplication(object sender, RoutedEventArgs e)
		{
			//if (IsBusyBuilding(true))
			//    return;
			//isbusy = true;

			var buildapps = GetBuildAppList_FromContextMenu(sender);
			BuildListOfApplications(buildapps);

			/*foreach (var buildapp in buildapps)
			{
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
			}*/
		}

		private void contextmenuOpenWithCSharpExpress(object sender, RoutedEventArgs e)
		{
			var buildapps = GetBuildAppList_FromContextMenu(sender);
			foreach (var buildapp in buildapps)
			{
				buildapp.OpenInCSharpExpress();
			}
		}

		private void contextmenuPublishOnline(object sender, RoutedEventArgs e)
		{
			var buildapps = GetBuildAppList_FromContextMenu(sender, false);//true);
			foreach (var buildapp in buildapps)
				PublishApp(buildapp, true);
		}

		private void PublishApp(BuildApplication buildapplication, bool waitUntilFinish = false)//, bool _32bitOnly = false)
		{
			//var buildapps = GetBuildAppList_FromContextMenu(sender);
			//foreach (var buildapplication in buildapps)
			//{
			MainWindow.SetWindowProgressState(TaskbarItemProgressState.Normal);
			MainWindow.SetWindowProgressValue(0);

			buildapplication.CurrentStatusText = "";
			ThreadingInterop.PerformOneArgFunctionSeperateThread<BuildApplication>(
				(buildapp) => buildapp.PerformPublishOnline(
					(mess, messagetype) => FeedbackMessageAction(buildapp, mess, messagetype),
					progperc => MainWindow.SetWindowProgressValue((double)progperc / 100D)),
			buildapplication,
			waitUntilFinish);
			//}
		}

		private void ContextmenuCheckForUpdates(object sender, RoutedEventArgs e)
		{
			var buildapps = GetBuildAppList_FromContextMenu(sender);
			foreach (var buildapp in buildapps)
			{
				AppCheckForUpdates(buildapp, true);
			}
		}

		private void ContextmenuInstallLatestVersion(object sender, RoutedEventArgs e)
		{
			var buildapps = GetBuildAppList_FromContextMenu(sender);
			foreach (var buildapp in buildapps)
			{
				ShowIndeterminateProgress("Installing latest version of " + buildapp.ApplicationName, buildapp, false);
				AutoUpdating.InstallLatest(buildapp.ApplicationName,
					err => FeedbackMessageAction(buildapp, err, FeedbackMessageTypes.Error),
					(completeAppname) =>
					{
						var ba = GetBuildApplicationFromApplicationName(completeAppname);
						AppCheckForUpdates(ba, true);
						HideIndeterminateProgress(null, true);
						HideIndeterminateProgress(ba, true);
					});
			}
		}

		private void FeedbackMessageAction(BuildApplication buildapp, string message, FeedbackMessageTypes feedbackType)
		{
			string mes = message;

			if (buildapp != null)
			{
				switch (feedbackType)
				{
					/*case FeedbackMessageTypes.Success: mes = message; break;
					case FeedbackMessageTypes.Error: mes = "ERROR: " + message; break;//UserMessages.ShowErrorMessage(mess); break;
					case FeedbackMessageTypes.Warning: mes = "WARNING: " + message; break; //UserMessages.ShowWarningMessage(mess); break;
					case FeedbackMessageTypes.Status: mes = message; break;
					default: UserMessages.ShowWarningMessage("Cannot use messagetype = " + feedbackType.ToString()); break;*/
					case FeedbackMessageTypes.Success:
						if (buildapp.CurrentStatus == BuildApplication.StatusTypes.Normal)//Only set success if its not Error/Warning
							buildapp.CurrentStatus = BuildApplication.StatusTypes.Success;
						break;
					case FeedbackMessageTypes.Error:
						buildapp.CurrentStatus = BuildApplication.StatusTypes.Error;
						break;
					case FeedbackMessageTypes.Warning:
						if (buildapp.CurrentStatus != BuildApplication.StatusTypes.Error)//Only set warning if its not Error
							buildapp.CurrentStatus = BuildApplication.StatusTypes.Warning;
						break;
					case FeedbackMessageTypes.Status:
						break;
					default:
						UserMessages.ShowWarningMessage("Cannot use messagetype = " + feedbackType.ToString());
						break;
				}
			}

			if (buildapp == null)
				this.Dispatcher.Invoke((Action)delegate { statusLabel.Text = mes; });
			else
				buildapp.AppendCurrentStatusText(mes);
		}

		private void contextmenuitemGetChangeLogs_OnlyAfterPreviousPublish_Click(object sender, RoutedEventArgs e)
		{
			var buildapps = GetBuildAppList_FromContextMenu(sender);
			GetChangelogsForBuildapps(buildapps, true);
		}

		private void ContextmenuitemGetChangeLogsAllClick(object sender, RoutedEventArgs e)
		{
			var buildapps = GetBuildAppList_FromContextMenu(sender);
			GetChangelogsForBuildapps(buildapps, false);
		}

		private bool _actionOnAppsAlreadyBusy = false;
		private void GetChangelogsForBuildapps(List<BuildApplication> buildapps, bool onlyAfterPreviousPublish)
		{
			if (_actionOnAppsAlreadyBusy)
			{
				UserMessages.ShowInfoMessage("Another action is already busy, please be patient...");
				return;
			}
			_actionOnAppsAlreadyBusy = true;

			ThreadingInterop.DoAction(delegate
			{
				try
				{
					Parallel.ForEach<BuildApplication>(
						buildapps,
						(buildapp) =>
						//foreach (var buildapp in buildapps)
						{
							DateTime? changelogsSinceDate = null;
							if (onlyAfterPreviousPublish)
							{
								if (!PublishInterop.ObtainPreviouslyPublishedDate(
									buildapp.ApplicationName,
									(mess, messagetype) => FeedbackMessageAction(buildapp, mess, messagetype),
									out changelogsSinceDate))
									return;//continue;
							}

							/*List<string> BugsFixed = null;
							List<string> Improvements = null;
							List<string> NewFeatures = null;*/
							var changeLogs = PublishInterop.GetChangeLogs(
								changelogsSinceDate,
								buildapp.ApplicationName,
								(mes, msgtype) => FeedbackMessageAction(null, mes, msgtype));
							if (changeLogs == null)
								return;

							foreach (var bugKey in changeLogs.BugsFixed.Keys)
								buildapp.AppendCurrentStatusText("BUG fixed: " + changeLogs.BugsFixed[bugKey].Summary
									+ " (" + changeLogs.BugsFixed[bugKey].Description + ") - " + string.Join(". ", changeLogs.BugsFixed[bugKey].TicketComments));
							foreach (var impKey in changeLogs.Improvements.Keys)
								buildapp.AppendCurrentStatusText("IMPROVEMENT done: " + changeLogs.Improvements[impKey].Summary
									+ " (" + changeLogs.Improvements[impKey].Description + ") - " + string.Join(". ", changeLogs.Improvements[impKey].TicketComments));
							foreach (var newKey in changeLogs.NewFeatures.Keys)
								buildapp.AppendCurrentStatusText("NEW feature: " + changeLogs.NewFeatures[newKey].Summary
									+ " (" + changeLogs.NewFeatures[newKey].Description + ") - " + string.Join(". ", changeLogs.NewFeatures[newKey].TicketComments));

							if ((changeLogs.BugsFixed == null || changeLogs.BugsFixed.Count == 0)
								&& (changeLogs.Improvements == null || changeLogs.Improvements.Count == 0)
								&& (changeLogs.NewFeatures == null || changeLogs.NewFeatures.Count == 0))
								buildapp.AppendCurrentStatusText("No tickets were [CLOSED] " + (onlyAfterPreviousPublish ? "after the last publish" : "yet") + ".");
						});
				}
				finally
				{
					_actionOnAppsAlreadyBusy = false;
				}
			},
			false);
		}

		private void ContextmenuCheckSubversionChanges(object sender, RoutedEventArgs e)
		{
			var buildapps = GetBuildAppList_FromContextMenu(sender);
			foreach (var buildapp in buildapps)
			{
				if (buildapp.IsVersionControlled != true)
					UserMessages.ShowWarningMessage("Directory is not version controlled: " + buildapp.GetSolutionDirectory());
				else
					AppCheckForSubversionChanges(buildapp, true);
			}
		}

		private void ContextmenuSubversionUpdate(object sender, RoutedEventArgs e)
		{
			var buildapps = GetBuildAppList_FromContextMenu(sender);
			foreach (var buildapp in buildapps)
			{
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
		}

		private void ContextmenuShowSubversionLog(object sender, RoutedEventArgs e)
		{
			var buildapps = GetBuildAppList_FromContextMenu(sender);
			foreach (var buildapp in buildapps)
			{
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
		}

		private void ContextmenuSubversionCommitChanges(object sender, RoutedEventArgs e)
		{
			var buildapps = GetBuildAppList_FromContextMenu(sender);
			foreach (var buildapp in buildapps)
			{
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
		}

		private void ContextmenuitemClearMessagesClick(object sender, RoutedEventArgs e)
		{
			var buildapps = GetBuildAppList_FromContextMenu(sender);
			foreach (var ba in buildapps)
				ba.ClearCurrentStatusText();
		}

		/*private void test_contextmenuitemCreateHtmlPage_Click(object sender, RoutedEventArgs e)
		{
			var buildapps = GetBuildAppList_FromContextMenu(sender);
			foreach (var ba in buildapps)
			{
				DateTime? changelogsSinceDate = null;
				if (!PublishInterop.ObtainPreviouslyPublishedDate(
					ba.ApplicationName,
					(mess, messagetype) => FeedbackMessageAction(ba, mess, messagetype),
					out changelogsSinceDate))
					return;//continue;
		 
				var changeLogs =PublishInterop.GetChangeLogs(
					changelogsSinceDate,
					ba.ApplicationName,
					(mes, msgtype) => { FeedbackMessageAction(null, mes, msgtype); });
				if (changeLogs == null)
					return;

				List<string> tmplistofscreenshots;//Will not use it
				var htmlPagePath = PublishInterop.CreateHtmlPageReturnFilename(ba.ApplicationName, "0.0.0.Test", "test_setup.exe", changeLogs, out tmplistofscreenshots, null);
				if (htmlPagePath == null)
					return;
				string newHtmlPagePath = Path.Combine(Path.GetDirectoryName(htmlPagePath), "testonly_" + Path.GetFileName(htmlPagePath));
				if (File.Exists(newHtmlPagePath))
					File.Delete(newHtmlPagePath);
				File.Move(htmlPagePath, newHtmlPagePath);
				Process.Start(newHtmlPagePath);
			}
		}*/

		private void TextblockAboutMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
		{
			AboutWindow2.ShowAboutWindow(new System.Collections.ObjectModel.ObservableCollection<DisplayItem>()
			{
				new DisplayItem("Author", "Francois Hill"),
				new DisplayItem("Icon(s) obtained from", "http://www.icons-land.com", "http://www.icons-land.com/vista-base-software-icons.php")
			});
		}

		private void ShowApplicationsBasedOnPredicate(Predicate<BuildApplication> predicate)
		{
			ForeachBuildappBorder((ba, border) =>
			{
				if (predicate(ba))
					border.Visibility = System.Windows.Visibility.Visible;
				else
					border.Visibility = System.Windows.Visibility.Collapsed;
			});
			tmpMainTreeview.UpdateLayout();
		}

		private void RadioButtonShowAllClick(object sender, RoutedEventArgs e)
		{
			ShowApplicationsBasedOnPredicate(ba => true);
		}

		private void RadioButtonShowAnalysedClick(object sender, RoutedEventArgs e)
		{
			ShowApplicationsBasedOnPredicate(ba => ba.IsPartOfAnalysedList());
		}

		private void RadioButtonShowUnanalysedClick(object sender, RoutedEventArgs e)
		{
			ShowApplicationsBasedOnPredicate(ba => !ba.IsPartOfAnalysedList());
		}

		private void RadioButtonShowDownloadableClick(object sender, RoutedEventArgs e)
		{
			ShowApplicationsBasedOnPredicate(ba => ba.IsPartOfDownloadableAllowedList());
		}

		private void RadioButtonShowNondownloadableClick(object sender, RoutedEventArgs e)
		{
			ShowApplicationsBasedOnPredicate(ba => !ba.IsPartOfDownloadableAllowedList());
		}

		private void RadioButtonShowInstalledClick(object sender, RoutedEventArgs e)
		{
			ShowApplicationsBasedOnPredicate(ba => ba.IsInstalled == true);
		}

		private void RadioButtonShowUninstalledClick(object sender, RoutedEventArgs e)
		{
			ShowApplicationsBasedOnPredicate(ba => ba.IsInstalled != true);
		}

		private void RadioButtonShowVersioncontrolledClick(object sender, RoutedEventArgs e)
		{
			ShowApplicationsBasedOnPredicate(ba => ba.IsVersionControlled == true);
		}

		private void RadioButtonShowUnversioncontrolledClick(object sender, RoutedEventArgs e)
		{
			ShowApplicationsBasedOnPredicate(ba => ba.IsVersionControlled != true);
		}

		private void RadioButtonShowSelectedClick(object sender, RoutedEventArgs e)
		{
			ShowApplicationsBasedOnPredicate(ba => ba.IsSelected == true);
		}

		private void RadioButtonShowUnselectedClick(object sender, RoutedEventArgs e)
		{
			ShowApplicationsBasedOnPredicate(ba => ba.IsSelected != true);
		}

		private void BorderMainItemBorderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			BuildApplication ba = GetBuildApplicationFromFrameworkElement(sender);
			if (ba == null) return;
			ba.IsSelected = ba.IsSelected != true;
			if (radioButtonShowSelected.IsChecked == true)
				ShowApplicationsBasedOnPredicate(b => b.IsSelected == true);
			else if (radioButtonShowUnselected.IsChecked == true)
				ShowApplicationsBasedOnPredicate(b => b.IsSelected != true);
		}

		private void ButtonSelectAllClick(object sender, RoutedEventArgs e)
		{
			foreach (var ba in listOfApplications)
				ba.IsSelected = true;
		}

		private void ButtonUnselectAllClick(object sender, RoutedEventArgs e)
		{
			foreach (var ba in listOfApplications)
				ba.IsSelected = false;
		}
	}

	public class BuildApplication : VsBuildProject, INotifyPropertyChanged
	{
		public enum StatusTypes { Normal, Success, Error, Warning };

		private string _applicationname;
		public override string ApplicationName { get { return _applicationname; } set { _applicationname = value; OnPropertyChanged("ApplicationName"); } }
		private string _currentstatustext;
		public override string CurrentStatusText { get { return _currentstatustext ?? ""; } set { _currentstatustext = value; OnPropertyChanged("CurrentStatusText", "HasFeedbackText"); } }
		public override bool HasFeedbackText { get { return !string.IsNullOrWhiteSpace(CurrentStatusText); } }
		/*private bool? _lastbuildresult;
		public override bool? LastBuildResult { get { return _lastbuildresult; } set { _lastbuildresult = value; OnPropertyChanged("LastBuildResult"); } }*/
		private StatusTypes _currentStatus;
		public StatusTypes CurrentStatus { get { return _currentStatus; } set { _currentStatus = value; OnPropertyChanged("CurrentStatus"); } }

		public bool? IsInstalled { get { return PublishInterop.IsInstalled(this.ApplicationName); } }
		public bool? IsVersionControlled { get { return OwnAppsInterop.DirIsValidSvnPath(Path.GetDirectoryName(this.SolutionFullpath)); } }

		private bool? _isselected;
		public bool? IsSelected { get { return _isselected; } set { _isselected = value; OnPropertyChanged("IsSelected"); } }

		private int? _currentprogressPprcentage;
		public int? CurrentProgressPercentage { get { return _currentprogressPprcentage; } set { _currentprogressPprcentage = value; OnPropertyChanged("CurrentProgressPercentage"); } }

		private string ApplicationIconPath { get; set; }

		private ImageSource _applicationicon;
		public ImageSource ApplicationIcon
		{
			get { if (_applicationicon == null) if (ApplicationIconPath != null) _applicationicon = IconsInterop.IconExtractor.Extract(ApplicationIconPath, IconsInterop.IconExtractor.IconSize.Large).IconToImageSource(); return _applicationicon; }
		}

		public bool IsFrancoisPc { get { return Directory.Exists(@"C:\Francois\Dev\VSprojects"); } }

		public BuildApplication(string applicationName, Action<string> actionOnError = null)
			: base(applicationName, null, actionOnError)
		{
			string tmper;

			this.CurrentProgressPercentage = 0;
			this.IsSelected = false;
			this.ApplicationIconPath = OwnAppsInterop.GetAppIconPath(applicationName, out tmper);
			if (this.ApplicationIconPath == null) if (actionOnError != null) actionOnError(tmper);
		}

		public override void ClearStatusText()
		{
			base.ClearStatusText();
			this.CurrentStatus = StatusTypes.Normal;
		}

		public bool IsPartOfAnalysedList()
		{
			return SettingsSimple.AnalyseProjectsSettings.Instance.ListOfApplicationsToAnalyse
				.Count(a => a.Equals(this.ApplicationName, StringComparison.InvariantCultureIgnoreCase)) > 0;
		}

		public bool IsPartOfDownloadableAllowedList()
		{
			return SettingsSimple.OnlineAppsSettings.Instance.AllowedListOfApplicationsToDownload
				.Count(a => a.Equals(this.ApplicationName, StringComparison.InvariantCultureIgnoreCase)) > 0;
		}

		public event PropertyChangedEventHandler PropertyChanged = new PropertyChangedEventHandler(delegate { });
		private void OnPropertyChanged(params string[] propertyNames) { foreach (var pn in propertyNames) PropertyChanged(this, new PropertyChangedEventArgs(pn)); }

		private static bool _isbusyBuilding = false;
		public static bool IsBusyBuilding(bool showErrorIfBusy = true)
		{
			if (_isbusyBuilding)
				UserMessages.ShowWarningMessage("Cannot build, another build already in progress");
			return _isbusyBuilding;
		}
		public static void SetIsBusyBuildingTrue(bool newValue)
		{
			_isbusyBuilding = newValue;
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

					if (IsBusyBuilding(true))
						return;
					_isbusyBuilding = true;

					try
					{
						List<string> csprojectPaths;
						this.PerformBuild(null, out csprojectPaths);
					}
					finally
					{
						_isbusyBuilding = false;
					}
				}
			},
			csharpPath,
			false);
		}

		public void AppendCurrentStatusText(string textToAppend)
		{
			if (!string.IsNullOrWhiteSpace(this.CurrentStatusText))
				this.CurrentStatusText += Environment.NewLine;
			this.CurrentStatusText += textToAppend;
		}

		public void ClearCurrentStatusText()
		{
			this.CurrentStatus = StatusTypes.Normal;
			this.CurrentStatusText = null;
			this.CurrentProgressPercentage = 0;//null will make it show Indeterminate
		}
	}

	public class StatusTypeToBrushConverter : IValueConverter
	//public class BoolToBrushConverter : IValueConverter
	{
		private static readonly GradientStopCollection SuccessColorStops =
            new GradientStopCollection(new GradientStop[]
 			{
				new GradientStop(Color.FromArgb(20, 0, 130, 0), 0),
				new GradientStop(Color.FromArgb(40, 0, 180, 0), 0.7),
				new GradientStop(Color.FromArgb(20, 0, 130, 0), 1)
			});
		private static readonly GradientStopCollection ErrorColorStops =
            new GradientStopCollection(new GradientStop[]
			{
				new GradientStop(Color.FromArgb(20, 130, 0, 0), 0),
				new GradientStop(Color.FromArgb(40, 180, 0, 0), 0.7),
				new GradientStop(Color.FromArgb(20, 130, 0, 0), 1)
			});
		private static readonly GradientStopCollection WarningColorStops =
            new GradientStopCollection(new GradientStop[]
			{
				new GradientStop(Color.FromArgb(20, 130, 0, 230), 0),
				new GradientStop(Color.FromArgb(40, 180, 0, 255), 0.7),
				new GradientStop(Color.FromArgb(20, 130, 0, 230), 1)
				/*new GradientStop(Color.FromArgb(20, 200, 150, 0), 0),
				new GradientStop(Color.FromArgb(40, 240, 150, 0), 0.7),
				new GradientStop(Color.FromArgb(20, 200, 150, 0), 1)*/
			});

		public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			if (!(value is BuildApplication.StatusTypes))
				return Brushes.Yellow;

			switch ((BuildApplication.StatusTypes)value)
			{
				case BuildApplication.StatusTypes.Normal:
					return Brushes.Transparent;
				case BuildApplication.StatusTypes.Success:
					return new LinearGradientBrush(SuccessColorStops, new Point(0, 0), new Point(0, 1));
				case BuildApplication.StatusTypes.Error:
					return new LinearGradientBrush(ErrorColorStops, new Point(0, 0), new Point(0, 1));
				case BuildApplication.StatusTypes.Warning:
					return new LinearGradientBrush(WarningColorStops, new Point(0, 0), new Point(0, 1));
				default:
					return Brushes.Transparent;
			}

			/*if (!(value is bool?) && value != null)//If null we assume its a null bool?
				return Brushes.Yellow;

			var boolval = (bool?)value;

			if (!boolval.HasValue)
				return Brushes.Transparent;
			else if (true == boolval.Value)
				return new LinearGradientBrush(SuccessColorStops, new Point(0, 0), new Point(0, 1));
			else
				return new LinearGradientBrush(ErrorColorStops, new Point(0, 0), new Point(0, 1));*/

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

			var boolval = (bool?)value;
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
			var paramIsOpposite = parameter != null && parameter.ToString().Equals("opposite", StringComparison.InvariantCultureIgnoreCase);
			if (value == null || !(value is int?))
			{
				return paramIsOpposite;
			}

			return !paramIsOpposite;
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
			var hideInsteadOfCollapse = parameter != null && parameter.ToString().Equals("HideInsteadOfCollapse", StringComparison.InvariantCultureIgnoreCase);
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

	/*public class BuildAppListHasSelectedItemsConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			object o = value;
			if (o != null)
			{
			}
			return null;
		}

		public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}*/
}
