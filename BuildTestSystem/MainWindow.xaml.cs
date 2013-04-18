using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
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

			BuildApplication.ActionOnFeedbackMessageReceived = OnFeedbackMessageReceived;
			BuildApplication.ActionOnProgressPercentageChanged = OnProgressPercentageChanged;
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


		private void OnFeedbackMessageReceived(VsBuildProject buildapp, string message, FeedbackMessageTypes messagetype)
		{
			if (buildapp == null)
				this.Dispatcher.BeginInvoke((Action)delegate
				{
					if (message != null)
						statusLabel.Text = message;
				});
			//else
			//{
			//buildapp.AppendCurrentStatusText(message);
			if (messagetype == FeedbackMessageTypes.Error)
				SetWindowProgressState(TaskbarItemProgressState.Error);//, OverlayImage.Error);
			else if (messagetype == FeedbackMessageTypes.Warning)
			{
				if (_lastProgressState != TaskbarItemProgressState.Error)
					SetWindowProgressState(TaskbarItemProgressState.Paused);//, OverlayImage.Warning);
			}
			else if (messagetype == FeedbackMessageTypes.Success)
			{
				if (_lastProgressState != TaskbarItemProgressState.Error
					&& _lastProgressState != TaskbarItemProgressState.Paused)
					SetWindowProgressState(TaskbarItemProgressState.Paused);//, OverlayImage.Success);
			}
			//}
		}
		private void OnProgressPercentageChanged(VsBuildProject buildapp, int? newprogress)
		{
			if (buildapp == null)
				this.Dispatcher.BeginInvoke((Action)delegate
				{
					if (!newprogress.HasValue)
						ShowIndeterminateProgress("");
					else HideIndeterminateProgress();
				});
		}

		private void DoOperationWithApps(IEnumerable<BuildApplication> appsList, Action<BuildApplication> actionOnEachApp,
			string OperationDisplayName, string InitialStatusMessage, bool AllowConcurrent,
			bool ClearAppStatusTextFirst, Predicate<BuildApplication> ShouldIncludeApp)
		{
			ResourceUsageTracker.LogStartOfOperationInApplication(OperationDisplayName);

			var settings = new VsBuildProject.OverallOperationSettings(
				   OperationDisplayName,
				   InitialStatusMessage,
				   AllowConcurrent,
				   (progperc, progstate) =>
				   {
					   SetWindowProgressValue((double)progperc / 100d);
					   if (progstate.HasValue)
						   SetWindowProgressState(GetTaskbarItemProgressStateFromProgressState(progstate.Value));
					   if (progperc == 100)
						   this.HideIndeterminateProgress(null, true);
				   },
				   (mes, msgtype) =>
					   this.Dispatcher.BeginInvoke((Action)delegate { this.statusLabel.Text = mes; }),//ShowIndeterminateProgress(mes, null, true),
				   ClearAppStatusTextFirst,
				   app => ShouldIncludeApp((BuildApplication)app),
				   VsBuildProject.OverallOperationSettings.DurationMeasurementType.Both);

			BuildApplication.DoOperation(
				appsList,
				(app) => actionOnEachApp((BuildApplication)app),
				settings);
		}

		public static void SetWindowProgressValue(double progressFractionOfOne)
		{
			_windowInstance.Dispatcher.BeginInvoke((Action<double>)(
				(progfact) =>
				{
					_windowTaskBarItem.ProgressValue = progfact;
				}),
				progressFractionOfOne);
		}

		private static OverlayImage? GetOverlayImageFromProgressState(TaskbarItemProgressState progressState)
		{
			switch (progressState)
			{
				case TaskbarItemProgressState.Error:
					return OverlayImage.Error;
				case TaskbarItemProgressState.Indeterminate:
					return null;
				case TaskbarItemProgressState.None:
					return null;
				case TaskbarItemProgressState.Normal:
					return null;
				case TaskbarItemProgressState.Paused:
					return OverlayImage.Warning;
				default:
					return OverlayImage.Error;
			}
		}

		//public enum OverlayImage { Success, BuildFailed, NotUpToDate, VersionControlChanges };
		public enum OverlayImage { Success, Error, Warning };
		private static TaskbarItemProgressState _lastProgressState = TaskbarItemProgressState.None;
		//private static OverlayImage? _lastOverlayImage = null;
		public static void SetWindowProgressState(TaskbarItemProgressState progressState)//, OverlayImage? overlayImage = null)
		{
			if (progressState == TaskbarItemProgressState.Indeterminate
				&& _lastProgressState != TaskbarItemProgressState.None)
				return;//We first have to set progressState to None and then we can go Indeterminate

			if (_lastProgressState == progressState)
				return;
			if (_lastProgressState == TaskbarItemProgressState.Indeterminate)
				SetWindowProgressValue(1);
			_lastProgressState = progressState;

			_windowInstance.Dispatcher.BeginInvoke((Action<TaskbarItemProgressState>)(//, OverlayImage?>)(
				(state) =>//, image) =>
				{
					_windowTaskBarItem.ProgressState = state;

					_windowTaskBarItem.Overlay = null;
					var image = GetOverlayImageFromProgressState(state);

					if (image != null)
					{
						string resourceKeyForOverlay = "";
						switch (image)
						{
							case OverlayImage.Success:
								resourceKeyForOverlay = "overlayImageSucccess";
								break;
							case OverlayImage.Warning:
								resourceKeyForOverlay = "overlayImageWarning";
								break;
							case OverlayImage.Error:
								resourceKeyForOverlay = "overlayImageError";
								break;
							/*case OverlayImage.BuildFailed:
								resourceKeyForOverlay = "overlayImageBuildFailed";
								break;
							case OverlayImage.NotUpToDate:
								resourceKeyForOverlay = "overlayImageNotUpToDate";
								break;
							case OverlayImage.VersionControlChanges:
								resourceKeyForOverlay = "overlayImageVersionControlChanges";
								break;*/
							default:
								break;
						}

						if (!string.IsNullOrWhiteSpace(resourceKeyForOverlay))
							_windowTaskBarItem.Overlay = (DrawingImage)_windowInstance.Resources[resourceKeyForOverlay];
					}
				}),
				progressState);/*,
				overlayImage);*/
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
					var newApp = new BuildApplication(app);
					//tmpMainListbox.Items.Add(newApp);
					//tmpMainTreeview.Items.Add(new BuildApplication(newApp));
					newApp.PropertyChanged += (sn, pn) =>
					{
						if (pn.PropertyName.Equals("IsSelected", StringComparison.InvariantCultureIgnoreCase))
						{
							UpdateControlsAffectedBySelection();
						}
						else if (pn.PropertyName.Equals("CurrentStatus", StringComparison.InvariantCultureIgnoreCase))
						{
							Dispatcher.BeginInvoke((Action)delegate
							{
								UpdateControlsAffectedBySelection();
								if (_lastUsedPredicateForShowingApps != null)
									ShowApplicationsBasedOnPredicate(_lastUsedPredicateForShowingApps);
								this.UpdateLayout();
							});
						}
					};
					listOfApplications.Add(newApp);
				}
				UpdateControlsAffectedBySelection();
			};

			if (Thread.CurrentThread != this.Dispatcher.Thread)
				this.Dispatcher.BeginInvoke(act);
			else
				act();
		}

		private int GetVisibleAppCount()
		{
			if (_lastUsedPredicateForShowingApps == null)
				return listOfApplications.Count;
			else
				return listOfApplications.Count(ba => _lastUsedPredicateForShowingApps(ba));
		}
		private void UpdateControlsAffectedBySelection()
		{
			int selectedCount = listOfApplications.Count(a => a.IsSelected == true);
			int unselectedCount = listOfApplications.Count(a => a.IsSelected == false);
			int invisibleCount = listOfApplications.Count(a => _lastUsedPredicateForShowingApps != null && !_lastUsedPredicateForShowingApps(a));

			buttonUnselectAll.Visibility =
				selectedCount > 0
				? Visibility.Visible
				: Visibility.Hidden;
			buttonSelectAll.Visibility =
				unselectedCount > 0
				&& invisibleCount == 0
				? Visibility.Visible
				: Visibility.Hidden;

			List<string> statusCounts = new List<string>();
			try
			{
				foreach (BuildApplication.StatusTypes statusType in Enum.GetValues(typeof(BuildApplication.StatusTypes)))
					statusCounts.Add(
						string.Format("{0} {1}",
						listOfApplications.Count(a => a.CurrentStatus == statusType), statusType.ToString()));

				textblockSelectedCount.Text = string.Format(
					"{0} Visible, {1} Selected, {2} Total\r\n{3}",
					GetVisibleAppCount(),
					selectedCount,
					listOfApplications.Count,
					string.Join(", ", statusCounts));

			}
			finally
			{
				statusCounts.Clear(); statusCounts = null;
			}
		}

		private void buttonBuildAll_Click(object sender, RoutedEventArgs e)
		{
			BuildListOfApplications(listOfApplications);
		}

		private void BuildListOfApplications(IEnumerable<BuildApplication> listOfAppsToBuild)
		{
			//Because we use 'VsBuildProject.PerformMultipleBuild', we cannot just port the code to 'this.DoOperationWithApps'
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
					(appwhichstarted) =>
					{
						ShowIndeterminateProgress("Building application: " + appwhichstarted.ApplicationName, (BuildApplication)appwhichstarted, true);
						stopwatches.Add((BuildApplication)appwhichstarted, Stopwatch.StartNew());
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
						//HideIndeterminateProgress((BuildApplication)appwhichbuildcomplete, true);
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
					MainWindow.SetWindowProgressState(TaskbarItemProgressState.Error);//, OverlayImage.Error);
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
			DoOperationWithApps(
				listOfApplications,
				(app) => app.CheckForUpdates(false),
				"Check for updates",
				"Busy checking for updates, please be patient...",
				true,
				true,
				(app) => ((BuildApplication)app).IsInstalled == true);

			/*if (BuildApplication.IsBusyBuilding(true))
				return;
			BuildApplication.SetIsBusyBuildingTrue(true);

			ShowIndeterminateProgress("Starting to check applications for updates, please wait...");
			ThreadingInterop.PerformVoidFunctionSeperateThread(() =>
			{
				var items = tmpMainTreeview.Items;
				//List<string> appswithErrors = new List<string>();
				for (int i = 0; i < items.Count; i++)
				{
					var ba = (items[i] as BuildApplication);
					if (ba.IsInstalled == true)
						ba.ResetStatus(true);
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
						buildapp.CheckForUpdates(false);
						//if (!string.IsNullOrWhiteSpace(buildapp.CurrentStatusText))
						//    appswithErrors.Add(buildapp.ApplicationName);
						//HideIndeterminateProgress(buildapp, true);
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
				//if (appswithErrors.Count > 0)
				//{
				//    MainWindow.SetWindowProgressValue(1);
				//    if (_lastProgressState != TaskbarItemProgressState.Error)
				//        MainWindow.SetWindowProgressState(TaskbarItemProgressState.Paused);//, OverlayImage.Warning);
				//    //UserMessages.ShowErrorMessage("Error building the following apps: " + Environment.NewLine +
				//    //    string.Join(Environment.NewLine, appswithErrors));
				//}
				//else
				//    MainWindow.SetWindowProgressState(TaskbarItemProgressState.None);
				HideIndeterminateProgress(null, true);
				BuildApplication.SetIsBusyBuildingTrue(false);
			},
			false);*/
		}

		private void buttonCheckVersioningStatusAll_Click(object sender, RoutedEventArgs e)
		{
			DoOperationWithApps(
				listOfApplications,
				app => app.CheckForGitChanges(false),
				"Checking updates",
				"Checking all applications for updates.",
				true,
				true,
				app => app.IsVersionControlled == true);

			/*if (BuildApplication.IsBusyBuilding(true))
				return;
			BuildApplication.SetIsBusyBuildingTrue(true);

			ShowIndeterminateProgress("Starting to check version control statusses, please wait...");

			ThreadingInterop.PerformVoidFunctionSeperateThread(() =>
			{
				var items = tmpMainTreeview.Items;
				List<string> appswithErrors = new List<string>();
				for (int i = 0; i < items.Count; i++)
					(items[i] as BuildApplication)
						.ResetStatus(true);

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
						buildapp.CheckForSubversionChanges(false);
						if (!string.IsNullOrWhiteSpace(buildapp.CurrentStatusText))
							appswithErrors.Add(buildapp.ApplicationName);
						//HideIndeterminateProgress(buildapp, true);
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
				//if (appswithErrors.Count > 0)
				//{
				//	MainWindow.SetWindowProgressValue(1);
				//	MainWindow.SetWindowProgressState(TaskbarItemProgressState.Error, OverlayImage.VersionControlChanges);
				//	//UserMessages.ShowErrorMessage("Error building the following apps: " + Environment.NewLine +
				//	//    string.Join(Environment.NewLine, appswithErrors));
				//}
				//else
				//	MainWindow.SetWindowProgressState(TaskbarItemProgressState.None);
				HideIndeterminateProgress(null, true);
				BuildApplication.SetIsBusyBuildingTrue(false);
			},
			false);*/
		}

		private TaskbarItemProgressState GetTaskbarItemProgressStateFromProgressState(VsBuildProject.ProgressStates progressState)
		{
			switch (progressState)
			{
				case VsBuildProject.ProgressStates.None:
					return TaskbarItemProgressState.None;
				case VsBuildProject.ProgressStates.Indeterminate:
					return TaskbarItemProgressState.Indeterminate;
				case VsBuildProject.ProgressStates.Normal:
					return TaskbarItemProgressState.Normal;
				case VsBuildProject.ProgressStates.Error:
					return TaskbarItemProgressState.Error;
				case VsBuildProject.ProgressStates.Paused:
					return TaskbarItemProgressState.Paused;
				default:
					return TaskbarItemProgressState.None;
			}
		}

		private void buttonTestClick(object sender, RoutedEventArgs e)
		{
			DoOperationWithApps(
				listOfApplications,
				(app) => { Thread.Sleep(500); },
				"Testing operation",
				"This is the initial message",
				true,
				true,
				(appcheckinclude) => { return appcheckinclude.ApplicationName.StartsWith("M", StringComparison.InvariantCultureIgnoreCase); });
		}

		private void ShowIndeterminateProgress(string message, BuildApplication buildappToSetProgress = null, bool fromSeparateThread = false)
		{
			SetWindowProgressState(TaskbarItemProgressState.Indeterminate);
			SetWindowProgressValue(0);

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
				this.Dispatcher.BeginInvoke(act);
		}

		private void HideIndeterminateProgress(BuildApplication buildappToSetProgress = null, bool fromSeparateThread = false)
		{
			if (buildappToSetProgress == null
				&& _lastProgressState == TaskbarItemProgressState.Indeterminate)
				SetWindowProgressState(TaskbarItemProgressState.None);

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
				this.Dispatcher.BeginInvoke(act);
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
			DoOperationWithApps(
				GetBuildAppList_FromContextMenu(sender),
				app => app.OpenInCSharpExpress(),
				"Opening in C#",
				"Opening selected applications in C# IDE",
				true,
				true,
				app => true);


			/*var buildapps = GetBuildAppList_FromContextMenu(sender);
			foreach (var buildapp in buildapps)
			{
				buildapp.OpenInCSharpExpress();
			}*/
		}

		private void contextmenuPublishOnline(object sender, RoutedEventArgs e)
		{
			DoOperationWithApps(
				GetBuildAppList_FromContextMenu(sender, false),
				app => PublishApp(app, true, true),
				"Publishing applications",
				"Starting to publish",
				false,//Cannot allow concurrent, cannot build simultaneously
				true,
				app => true);

			/*var buildapps = GetBuildAppList_FromContextMenu(sender, false);//true);
			foreach (var buildapp in buildapps)
				PublishApp(buildapp, true, true);*/
		}

		private void contextmenuPublishOnlineButDoNotRunAfterInstallingSilently(object sender, RoutedEventArgs e)
		{
			DoOperationWithApps(
				GetBuildAppList_FromContextMenu(sender, false),
				app => PublishApp(app, true, false),
				"Publishing applications (without running afterwards)",
				"Starting to publish (will not run afterwards)",
				false,//Cannot allow concurrent, cannot build simultaneously
				true,
				app => true);

			/*var buildapps = GetBuildAppList_FromContextMenu(sender, false);//true);
			foreach (var buildapp in buildapps)
				PublishApp(buildapp, true, false);*/
		}

		private void PublishApp(BuildApplication buildapplication, bool waitUntilFinish = false, bool runAfterInstallingSilently = true)//, bool _32bitOnly = false)
		{
			//var buildapps = GetBuildAppList_FromContextMenu(sender);
			//foreach (var buildapplication in buildapps)
			//{
			MainWindow.SetWindowProgressState(TaskbarItemProgressState.Normal);
			MainWindow.SetWindowProgressValue(0);

			ThreadingInterop.PerformOneArgFunctionSeperateThread<BuildApplication>(
				(buildapp) => buildapp.PerformPublishOnline(runAfterInstallingSilently),
			buildapplication,
			waitUntilFinish);
			//}
		}

		private void ContextmenuCheckForUpdates(object sender, RoutedEventArgs e)
		{
			DoOperationWithApps(
				GetBuildAppList_FromContextMenu(sender),
				app => app.CheckForUpdates(false),
				"Check for updates",
				"Starting to check for updates",
				true,
				true,
				app => app.IsInstalled == true);

			/*var buildapps = GetBuildAppList_FromContextMenu(sender);
			foreach (var buildapp in buildapps)
				buildapp.CheckForUpdates(true);*/
		}

		private void ContextmenuInstallLatestVersion(object sender, RoutedEventArgs e)
		{
			DoOperationWithApps(
				GetBuildAppList_FromContextMenu(sender),
				app => app.InstallLatest(null),
				"Install latest version",
				"Installing latest version",
				true,
				true,
				app => true);

			/*var buildapps = GetBuildAppList_FromContextMenu(sender);
			foreach (var buildapp in buildapps)
			{
				ShowIndeterminateProgress("Installing latest version of " + buildapp.ApplicationName, buildapp, false);
				buildapp.InstallLatest(
					(ba) =>
					{
						HideIndeterminateProgress(null, true);
						HideIndeterminateProgress(ba, true);
					});
			}*/
		}

		/*private void FeedbackMessageAction(BuildApplication buildapp, string message, FeedbackMessageTypes feedbackType)
		{
			string mes = message;

			if (buildapp != null)
			{
				switch (feedbackType)
				{
					//case FeedbackMessageTypes.Success: mes = message; break;
					//case FeedbackMessageTypes.Error: mes = "ERROR: " + message; break;//UserMessages.ShowErrorMessage(mess); break;
					//case FeedbackMessageTypes.Warning: mes = "WARNING: " + message; break; //UserMessages.ShowWarningMessage(mess); break;
					//case FeedbackMessageTypes.Status: mes = message; break;
					//default: UserMessages.ShowWarningMessage("Cannot use messagetype = " + feedbackType.ToString()); break;
		 
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
				this.Dispatcher.BeginInvoke((Action)delegate { statusLabel.Text = mes; });
			else
				buildapp.AppendCurrentStatusText(mes);
		}*/

		private void contextmenuitemGetChangeLogs_OnlyAfterPreviousPublish_Click(object sender, RoutedEventArgs e)
		{
			DoOperationWithApps(
				GetBuildAppList_FromContextMenu(sender),
				app => app.GetChangeLogs(true),
				"Get changelogs (only after previous publish)",
				"Starting to get changelogs (only after previous publish)",
				true,
				true,
				app => true);

			/*var buildapps = GetBuildAppList_FromContextMenu(sender);
			GetChangelogsForBuildapps(buildapps, true);*/
		}

		private void ContextmenuitemGetChangeLogsAllClick(object sender, RoutedEventArgs e)
		{
			DoOperationWithApps(
				GetBuildAppList_FromContextMenu(sender),
				app => app.GetChangeLogs(false),
				"Get changelogs (from beginning of time)",
				"Starting to get changelogs (from beginning of time)",
				true,
				true,
				app => true);

			/*var buildapps = GetBuildAppList_FromContextMenu(sender);
			GetChangelogsForBuildapps(buildapps, false);*/
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
							buildapp.GetChangeLogs(onlyAfterPreviousPublish);
						});
				}
				finally
				{
					_actionOnAppsAlreadyBusy = false;
				}
			},
			false);
		}

		private void ContextmenuCheckGitChanges(object sender, RoutedEventArgs e)
		{
			DoOperationWithApps(
				GetBuildAppList_FromContextMenu(sender),
				app => app.CheckForGitChanges(false),
				"Checking git changes",
				"Starting to check for git changes.",
				true,
				true,
				app => app.IsVersionControlled == true);

			/*var buildapps = GetBuildAppList_FromContextMenu(sender);
			foreach (var buildapp in buildapps)
			{
				if (buildapp.IsVersionControlled != true)
					UserMessages.ShowWarningMessage("Directory is not version controlled: " + buildapp.GetSolutionDirectory());
				else
					buildapp.CheckForSubversionChanges(true);
			}*/
		}

		private void ContextmenuGitPull(object sender, RoutedEventArgs e)
		{
			DoOperationWithApps(
				GetBuildAppList_FromContextMenu(sender),
				app =>
				{
					TortoiseProcInterop.Git_StartTortoiseProc(TortoiseProcInterop.TortoiseGitCommands.Pull, app.GetSolutionDirectory())
						.WaitForExit();
					app.CheckForGitChanges(false);
				},
				"Show git udpates dialog",
				"Starting to show git updates dialog.",
				true,
				true,
				app => app.IsVersionControlled == true);

			/*var buildapps = GetBuildAppList_FromContextMenu(sender);
			foreach (var buildapp in buildapps)
			{
				if (buildapp.IsVersionControlled != true)
					UserMessages.ShowWarningMessage("Directory is not version controlled: " + buildapp.GetSolutionDirectory());
				else
				{
					ThreadingInterop.PerformOneArgFunctionSeperateThread<BuildApplication>(
						(b) =>
						{
							Process p = TortoiseProcInterop.Git_StartTortoiseProc(TortoiseProcInterop.TortoiseSvnCommands.Update, buildapp.GetSolutionDirectory());
							p.WaitForExit();
							b.CheckForSubversionChanges(false);
						},
						buildapp,
						false);
				}
			}*/
		}

		private void ContextmenuShowGitLog(object sender, RoutedEventArgs e)
		{
			DoOperationWithApps(
				GetBuildAppList_FromContextMenu(sender),
				app =>
				{
					TortoiseProcInterop.Git_StartTortoiseProc(TortoiseProcInterop.TortoiseGitCommands.Log, app.GetSolutionDirectory())
						.WaitForExit();
					app.CheckForGitChanges(false);
				},
				"Show git log",
				"Starting to show git log.",
				true,
				true,
				app => app.IsVersionControlled == true);

			/*var buildapps = GetBuildAppList_FromContextMenu(sender);
			foreach (var buildapp in buildapps)
			{
				if (buildapp.IsVersionControlled != true)
					UserMessages.ShowWarningMessage("Directory is not version controlled: " + buildapp.GetSolutionDirectory());
				else
				{
					ThreadingInterop.PerformOneArgFunctionSeperateThread<BuildApplication>(
						(b) =>
						{
							Process p = TortoiseProcInterop.Git_StartTortoiseProc(TortoiseProcInterop.TortoiseSvnCommands.Log, buildapp.GetSolutionDirectory());
							p.WaitForExit();
							b.CheckForSubversionChanges(false);
						},
						buildapp,
						false);
				}
			}*/
		}

		private void ContextmenuGitCommitLocallySameMessage(object sender, RoutedEventArgs e)
		{
			var msg = InputBoxWPF.Prompt("Please enter the git commit message to be used for all", "Common commit message");
			if (msg == null) return;

			var buildapps = GetBuildAppList_FromContextMenu(sender);

			DoOperationWithApps(
				buildapps,
				(app) => app.CommitMessage(msg),
				"Commit same git message",
				"Busy committing git message, please be patient...",
				false,
				true,
				(app) => ((BuildApplication)app).IsVersionControlled == true);
		}

		private void ContextmenuGitCommitLocallyChanges(object sender, RoutedEventArgs e)
		{
			DoOperationWithApps(
				GetBuildAppList_FromContextMenu(sender),
				app =>
				{
					TortoiseProcInterop.Git_StartTortoiseProc(
						TortoiseProcInterop.TortoiseGitCommands.Commit,
						app.GetSolutionDirectory())
						.WaitForExit();
					app.CheckForGitChanges(false);
				},
				"Show git commit dialog",
				"Starting to show git commit dialog.",
				true,
				true,
				app => app.IsVersionControlled == true);

			/*var buildapps = GetBuildAppList_FromContextMenu(sender);
			foreach (var buildapp in buildapps)
			{
				if (buildapp.IsVersionControlled != true)
					UserMessages.ShowWarningMessage("Directory is not version controlled: " + buildapp.GetSolutionDirectory());
				else
				{
					ThreadingInterop.PerformOneArgFunctionSeperateThread<BuildApplication>(
						(b) =>
						{
							Process p = TortoiseProcInterop.Git_StartTortoiseProc(TortoiseProcInterop.TortoiseSvnCommands.Commit, buildapp.GetSolutionDirectory());
							p.WaitForExit();
							b.CheckForSubversionChanges(false);
						},
						buildapp,
						false);
				}
			}*/
		}

		private void ContextmenuGitPushWithoutGui(object sender, RoutedEventArgs e)
		{
			var buildapps = GetBuildAppList_FromContextMenu(sender);

			DoOperationWithApps(
				buildapps,
				(app) => app.PushWithoutGui(),
				"Push (no GUI)",
				"Busy pushing (no GUI) to default git remote, please be patient...",
				false,//true, We need the Environment.CurrentDirectory, so we cannot run concurrent
				true,
				(app) => ((BuildApplication)app).IsVersionControlled == true);
		}

		private void ContextmenuGitPush(object sender, RoutedEventArgs e)
		{
			DoOperationWithApps(
				   GetBuildAppList_FromContextMenu(sender),
				   app =>
				   {
					   TortoiseProcInterop.Git_StartTortoiseProc(
						   TortoiseProcInterop.TortoiseGitCommands.Push,
						   app.GetSolutionDirectory())
						   .WaitForExit();
					   app.CheckForGitChanges(false);
				   },
				   "Show git commit dialog",
				   "Starting to show git commit dialog.",
				   true,
				   true,
				   app => app.IsVersionControlled == true);
		}

		private void ContextmenuitemClearMessagesClick(object sender, RoutedEventArgs e)
		{
			var buildapps = GetBuildAppList_FromContextMenu(sender);
			foreach (var ba in buildapps)
				ba.ResetStatus(false);
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

		private Predicate<BuildApplication> _lastUsedPredicateForShowingApps = null;
		private void ShowApplicationsBasedOnPredicate(Predicate<BuildApplication> predicate)
		{
			if (predicate == null)
				return;
			_lastUsedPredicateForShowingApps = predicate;

			ForeachBuildappBorder((ba, border) =>
			{
				ba.IsSelected = false;//Unselect all when visibility changes
				if (predicate(ba))
					border.Visibility = System.Windows.Visibility.Visible;
				else
					border.Visibility = System.Windows.Visibility.Collapsed;
			});
			tmpMainTreeview.UpdateLayout();
			UpdateControlsAffectedBySelection();
		}

		private void RadioButtonShowAllClick(object sender, RoutedEventArgs e)
		{
			ShowApplicationsBasedOnPredicate(ba => true);
		}

		private void RadioButtonShowNormalClick(object sender, RoutedEventArgs e)
		{
			ShowApplicationsBasedOnPredicate(ba => ba.CurrentStatus == VsBuildProject.StatusTypes.Normal);
		}

		private void RadioButtonShowNonnormalClick(object sender, RoutedEventArgs e)
		{
			ShowApplicationsBasedOnPredicate(ba => ba.CurrentStatus != VsBuildProject.StatusTypes.Normal);
		}

		private void RadioButtonShowQueuedClick(object sender, RoutedEventArgs e)
		{
			ShowApplicationsBasedOnPredicate(ba => ba.CurrentStatus == VsBuildProject.StatusTypes.Queued);
		}

		private void RadioButtonShowBusyClick(object sender, RoutedEventArgs e)
		{
			ShowApplicationsBasedOnPredicate(ba => ba.CurrentStatus == VsBuildProject.StatusTypes.Busy);
		}

		private void RadioButtonShowSuccessClick(object sender, RoutedEventArgs e)
		{
			ShowApplicationsBasedOnPredicate(ba => ba.CurrentStatus == VsBuildProject.StatusTypes.Success);
		}

		private void RadioButtonShowWarningsClick(object sender, RoutedEventArgs e)
		{
			ShowApplicationsBasedOnPredicate(ba => ba.CurrentStatus == VsBuildProject.StatusTypes.Warning);
		}

		private void RadioButtonShowErrorsClick(object sender, RoutedEventArgs e)
		{
			ShowApplicationsBasedOnPredicate(ba => ba.CurrentStatus == VsBuildProject.StatusTypes.Error);
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

		private void ButtonSelectVisibleClick(object sender, RoutedEventArgs e)
		{
			foreach (var ba in listOfApplications)
				if (_lastUsedPredicateForShowingApps == null
					|| _lastUsedPredicateForShowingApps(ba))
					ba.IsSelected = true;
		}

		private void ButtonUnselectAllClick(object sender, RoutedEventArgs e)
		{
			foreach (var ba in listOfApplications)
				ba.IsSelected = false;
		}

		private void Button_Click(object sender, RoutedEventArgs e)
		{
			statusLabel.Text = null;
			statusLabel.UpdateLayout();
		}

		private void buttonExpandSelected_Click(object sender, RoutedEventArgs e)
		{
			this.ForeachBuildapp(ba => { if (ba.IsSelected == true) ba.IsFeedbackExpanded = true; });
		}

		private void buttonCollapseSelected_Click(object sender, RoutedEventArgs e)
		{
			this.ForeachBuildapp(ba => { if (ba.IsSelected == true) ba.IsFeedbackExpanded = false; });
		}
	}

	public class BuildApplication : VsBuildProject, INotifyPropertyChanged
	{
		private string _applicationname;
		public override string ApplicationName { get { return _applicationname; } set { _applicationname = value; OnPropertyChanged(ba => ba.ApplicationName); } }
		private string _currentstatustext;
		public override string CurrentStatusText { get { return _currentstatustext ?? ""; } protected set { _currentstatustext = value; OnPropertyChanged(ba => ba.CurrentStatusText); } }/*, "HasFeedbackText"); } }
		public override bool HasFeedbackText { get { return !string.IsNullOrWhiteSpace(CurrentStatusText); } }*/

		/*private bool? _lastbuildresult;
		public override bool? LastBuildResult { get { return _lastbuildresult; } set { _lastbuildresult = value; OnPropertyChanged("LastBuildResult"); } }*/
		private StatusTypes _currentStatus;
		public override StatusTypes CurrentStatus { get { return _currentStatus; } set { _currentStatus = value; OnPropertyChanged(ba => ba.CurrentStatus); } }

		private string _lasterror;
		public override string LastError { get { return _lasterror; } set { _lasterror = value; OnPropertyChanged(ba => ba.LastError); } }

		private string _lastsuccess;
		public override string LastSuccess { get { return _lastsuccess; } set { _lastsuccess = value; OnPropertyChanged(ba => ba.LastSuccess); } }

		public bool? IsInstalled { get { return PublishInterop.IsInstalled(this.ApplicationName); } }
		public bool? IsVersionControlled { get { return OwnAppsInterop.DirIsValidGitPath(Path.GetDirectoryName(this.SolutionFullpath)); } }

		private bool? _isselected;
		public bool? IsSelected { get { return _isselected; } set { _isselected = value; OnPropertyChanged(ba => ba.IsSelected); } }

		private int? _currentprogressPprcentage;
		public override int? CurrentProgressPercentage { get { return _currentprogressPprcentage; } set { _currentprogressPprcentage = value; OnPropertyChanged(ba => ba.CurrentProgressPercentage); } }

		private bool _isfeedbackexpanded;
		public bool IsFeedbackExpanded { get { return _isfeedbackexpanded; } set { _isfeedbackexpanded = value; OnPropertyChanged(ba => ba.IsFeedbackExpanded); } }

		private string ApplicationIconPath { get; set; }

		private ImageSource _applicationicon;
		public ImageSource ApplicationIcon
		{
			get { if (_applicationicon == null) if (ApplicationIconPath != null) _applicationicon = IconsInterop.IconExtractor.Extract(ApplicationIconPath, IconsInterop.IconExtractor.IconSize.Large).IconToImageSource(); return _applicationicon; }
		}

		public bool IsFrancoisPc { get { return Directory.Exists(@"C:\Francois\Dev\VSprojects"); } }

		public BuildApplication(string applicationName)
			: base(applicationName, null)
		{
			string tmper;

			this.CurrentProgressPercentage = 0;
			this.IsSelected = false;
			this.ApplicationIconPath = OwnAppsInterop.GetAppIconPath(applicationName, out tmper);
			if (this.ApplicationIconPath == null) OnFeedbackMessage(tmper, FeedbackMessageTypes.Error);
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
		private void OnPropertyChanged(params Expression<Func<BuildApplication, object>>[] propertiesOrFieldsAsExpressions)
		{
			ReflectionInterop.DoForeachPropertOrField<BuildApplication>(
				this,
				propertiesOrFieldsAsExpressions,
				(instanceObj, memberInfo, memberValue) =>
				{
					PropertyChanged(instanceObj, new PropertyChangedEventArgs(memberInfo.Name));
				});
		}

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

		public void InstallLatest(Action<BuildApplication> actionOnComplete, bool installSilently = true)
		{
			AutoUpdating.InstallLatest(this.ApplicationName, OnErrorMessage, delegate { if (actionOnComplete != null) actionOnComplete(this); }, installSilently);
		}

		public void GetChangeLogs(bool onlyAfterPreviousPublish)
		{
			DateTime? changelogsSinceDate = null;
			if (onlyAfterPreviousPublish)
			{
				if (!PublishInterop.ObtainPreviouslyPublishedDate(
					this.ApplicationName,
					OnFeedbackMessage,
					out changelogsSinceDate))
					return;//continue;
			}

			/*List<string> BugsFixed = null;
			List<string> Improvements = null;
			List<string> NewFeatures = null;*/
			var changeLogs = PublishInterop.GetChangeLogs(
				changelogsSinceDate,
				this.ApplicationName,
				OnFeedbackMessage);
			if ((changeLogs == null)
				|| ((changeLogs.BugsFixed == null || changeLogs.BugsFixed.Count == 0)
					&& (changeLogs.Improvements == null || changeLogs.Improvements.Count == 0)
					&& (changeLogs.NewFeatures == null || changeLogs.NewFeatures.Count == 0)))
				return;

			foreach (var bugKey in changeLogs.BugsFixed.Keys)
				this.OnFeedbackMessage("BUG fixed: " + changeLogs.BugsFixed[bugKey].Summary
					+ " (" + changeLogs.BugsFixed[bugKey].Description + ") - " + string.Join(". ", changeLogs.BugsFixed[bugKey].TicketComments), FeedbackMessageTypes.Status);
			foreach (var impKey in changeLogs.Improvements.Keys)
				this.OnFeedbackMessage("IMPROVEMENT done: " + changeLogs.Improvements[impKey].Summary
					+ " (" + changeLogs.Improvements[impKey].Description + ") - " + string.Join(". ", changeLogs.Improvements[impKey].TicketComments), FeedbackMessageTypes.Status);
			foreach (var newKey in changeLogs.NewFeatures.Keys)
				this.OnFeedbackMessage("NEW feature: " + changeLogs.NewFeatures[newKey].Summary
					+ " (" + changeLogs.NewFeatures[newKey].Description + ") - " + string.Join(". ", changeLogs.NewFeatures[newKey].TicketComments), FeedbackMessageTypes.Status);
		}

		public void OpenInCSharpExpress()
		{
			var cspath = RegistryInterop.GetAppPathFromRegistry("VCSExpress.exe");
			if (cspath == null)
			{
				UserMessages.ShowErrorMessage("Cannot obtain CSharp Express path from registry.");
				return;
			}

			string csharpPath = cspath;
			//ThreadingInterop.PerformOneArgFunctionSeperateThread<string>((csharpPath) =>
			//{
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
					this.PerformBuild(out csprojectPaths);
				}
				finally
				{
					_isbusyBuilding = false;
				}
			}
			//},
			//cspath,
			//false);
		}

		public void CheckForGitChanges(bool separateThread)
		{
			Action<BuildApplication> checkForGitChanges =
                (buildApplication) =>
				{
					buildApplication.ResetStatus(true);
					buildApplication.MarkAsBusy();

					string changesText;
					if (TortoiseProcInterop.CheckFolderGitChanges(Path.GetDirectoryName(this.SolutionFullpath), out changesText))
						OnFeedbackMessage(changesText, FeedbackMessageTypes.Warning);
					else
						OnFeedbackMessage("No changes.", FeedbackMessageTypes.Success);//buildApplication.CurrentStatus = BuildApplication.StatusTypes.Success;

					buildApplication.MarkAsComplete();
				};

			if (separateThread)
				ThreadingInterop.PerformOneArgFunctionSeperateThread<BuildApplication>(checkForGitChanges, this, true);
			else
				checkForGitChanges(this);
		}

		public void CheckForUpdates(bool separateThread)
		{
			/*Action<BuildApplication> checkForUpdatesAction =
                (buildApplication) =>
				{
					buildApplication.ResetStatus(true);
					buildApplication.MarkAsBusy();*/

			string appExePath = PublishInterop.GetApplicationExePathFromApplicationName(this.ApplicationName);
			string InstalledVersion =
                        File.Exists(appExePath)
				? FileVersionInfo.GetVersionInfo(appExePath).FileVersion
				: "0.0.0.0";
			string errorIfNull;
			SharedClasses.AutoUpdating.MockPublishDetails onlineVersionDetails;
			bool? checkSuccess =
				AutoUpdating.CheckForUpdatesSilently(this.ApplicationName, InstalledVersion, out errorIfNull, out onlineVersionDetails);
			if (checkSuccess == true)//Is up to date
			{
				this.CurrentStatus = BuildApplication.StatusTypes.Success;
			}
			else if (checkSuccess == false)//Newer version available
			{
				this.CurrentStatus = StatusTypes.Warning;
				this.AppendCurrentStatusText("Newer version available: " + onlineVersionDetails.ApplicationVersion);
			}
			else//Unable to check for updates
			{
				OnFeedbackMessage(errorIfNull, FeedbackMessageTypes.Error);
				/*buildApplication.CurrentStatusText
					"ERROR: " + errorIfNull;*/
			}

			/*buildApplication.MarkAsComplete();
		};

	if (separateThread)
		ThreadingInterop.PerformOneArgFunctionSeperateThread<BuildApplication>(checkForUpdatesAction, this, false);
	else
		checkForUpdatesAction(this);*/
		}

		private static FeedbackMessageTypes GetFeedbackMessageTypesFromTextFeedbackType(TextFeedbackType tft)
		{
			switch (tft)
			{
				case TextFeedbackType.Error:
					return FeedbackMessageTypes.Error;
				case TextFeedbackType.Success:
					return FeedbackMessageTypes.Success;
				case TextFeedbackType.Noteworthy:
					return FeedbackMessageTypes.Status;//FeedbackMessageTypes.Warning;
				case TextFeedbackType.Subtle:
					return FeedbackMessageTypes.Status;
			}
			return FeedbackMessageTypes.Error;
		}

		public void CommitMessage(string commitMessage)
		{
			string changesText;
			if (TortoiseProcInterop.CheckFolderGitChanges(this.GetSolutionDirectory(), out changesText))
			{
				//we don't make use currently of the returned messages from the following method
				string errorIfFailed;
				List<string> outputs, errors;
				bool? runsuccess = GitInterop.PerformGitCommand(
					this.GetSolutionDirectory(),
					GitInterop.GitCommand.Commit,
					out errorIfFailed,
					out outputs,
					out errors,
					commitMessage);

				if (runsuccess != true)
				{
					if (errorIfFailed != null)
						OnFeedbackMessage(errorIfFailed, FeedbackMessageTypes.Error);
					else
						OnFeedbackMessage(string.Join(Environment.NewLine, outputs.Concat(errors)), FeedbackMessageTypes.Warning);
				}

				//if (TortoiseProcInterop.CheckFolderSubversionChanges(this.GetSolutionDirectory(), out changesText))
				if (TortoiseProcInterop.CheckFolderGitChanges(this.GetSolutionDirectory(), out changesText))
					OnFeedbackMessage(changesText, FeedbackMessageTypes.Warning);
				else
					OnFeedbackMessage(null, FeedbackMessageTypes.Success);
			}
			else
				OnFeedbackMessage("Application had no git changes", FeedbackMessageTypes.Warning);
		}

		public void PushWithoutGui()
		{
			//we don't make use currently of the returned messages from the following method
			string errorIfFailed;
			List<string> outputs, errors;
			bool? runsuccess = GitInterop.PerformGitCommand(
				this.GetSolutionDirectory(),
				GitInterop.GitCommand.Push,
				out errorIfFailed,
				out outputs,
				out errors,
				this.ApplicationName);//Remote name

			if (runsuccess != true)
			{
				if (errorIfFailed != null)
					OnFeedbackMessage(errorIfFailed, FeedbackMessageTypes.Error);
				else if (errors != null
					&& errors.Last().Trim().Equals("Everything up-to-date", StringComparison.InvariantCultureIgnoreCase))
					OnFeedbackMessage(string.Join(Environment.NewLine, "...", "Everything up-to-date"), FeedbackMessageTypes.Success);
				else
					OnFeedbackMessage(string.Join(Environment.NewLine, outputs.Concat(errors)), FeedbackMessageTypes.Warning);
			}
		}
	}

	public class StatusTypeToBrushConverter : IValueConverter
	//public class BoolToBrushConverter : IValueConverter
	{
		/*private static readonly GradientStopCollection QueuedColorStops =
            new GradientStopCollection(new GradientStop[]
 			{
				new GradientStop(Color.FromArgb(30, 130, 130, 130), 0),
				new GradientStop(Color.FromArgb(60, 180, 180, 180), 0.7),
				new GradientStop(Color.FromArgb(30, 130, 130, 130), 1)
			});
		private static readonly GradientStopCollection BusyColorStops =
            new GradientStopCollection(new GradientStop[]
 			{
				new GradientStop(Color.FromArgb(30, 200, 200, 0), 0),
				new GradientStop(Color.FromArgb(60, 255, 255, 0), 0.7),
				new GradientStop(Color.FromArgb(30, 200, 200, 0), 1)
			});
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
				new GradientStop(Color.FromArgb(60, 130, 0, 0), 1),
				new GradientStop(Color.FromArgb(0, 180, 0, 0), 0.2),
				new GradientStop(Color.FromArgb(60, 130, 0, 0), 1)
			});
		private static readonly GradientStopCollection WarningColorStops =
            new GradientStopCollection(new GradientStop[]
			{
				new GradientStop(Color.FromArgb(20, 130, 0, 230), 0),
				new GradientStop(Color.FromArgb(40, 180, 0, 255), 0.7),
				new GradientStop(Color.FromArgb(20, 130, 0, 230), 1)
				//new GradientStop(Color.FromArgb(20, 200, 150, 0), 0),
				//new GradientStop(Color.FromArgb(40, 240, 150, 0), 0.7),
				//new GradientStop(Color.FromArgb(20, 200, 150, 0), 1)
			});*/

		public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			if (!(value is BuildApplication.StatusTypes))
				return Colors.Gold;

			switch ((BuildApplication.StatusTypes)value)
			{
				case BuildApplication.StatusTypes.Normal:
					return Colors.Transparent;
				case VsBuildProject.StatusTypes.Queued:
					return Colors.Gray;//new LinearGradientBrush(QueuedColorStops, new Point(0, 0), new Point(1, 0));
				case VsBuildProject.StatusTypes.Busy:
					return Colors.Gold;//new LinearGradientBrush(BusyColorStops, new Point(0, 0), new Point(1, 0));
				case BuildApplication.StatusTypes.Success:
					return Colors.Green;//new LinearGradientBrush(SuccessColorStops, new Point(0, 0), new Point(1, 0));
				case BuildApplication.StatusTypes.Error:
					return Colors.Red;//new LinearGradientBrush(ErrorColorStops, new Point(0, 0), new Point(1, 0));
				case BuildApplication.StatusTypes.Warning:
					return Colors.Orange;//new LinearGradientBrush(WarningColorStops, new Point(0, 0), new Point(1, 0));
				default:
					return Colors.Transparent;
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

	/*public class BoolToOpacityConverter : IValueConverter
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
	}*/

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
