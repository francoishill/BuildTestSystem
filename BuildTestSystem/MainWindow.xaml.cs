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
				var apps = OnlineSettings.BuildTestSystemSettings.Instance.ListOfApplicationsToBuild;
				OnlineSettings.BuildTestSystemSettings.EnsureDefaultItemsInList();
				HideIndeterminateProgress(true);
			},
			false);
		}

		private void buttonObtainApplicationList_Click(object sender, RoutedEventArgs e)
		{
			tmpMainListbox.Items.Clear();
			var applicationlist = OnlineSettings.BuildTestSystemSettings.Instance.ListOfApplicationsToBuild;
			foreach (var app in applicationlist)
			{
				//tmpMainListbox.Items.Add(app);
				tmpMainListbox.Items.Add(new BuildApplication(app));
			}
		}

		bool busybuilding = false;
		private bool IsBusyBuilding(bool showErrorIfBusy = true)
		{
			if (busybuilding)
				UserMessages.ShowWarningMessage("Cannot build, another build already in progress");
			return busybuilding;
		}

		private void buttonBuildAll_Click(object sender, RoutedEventArgs e)
		{
			if (IsBusyBuilding(true))
				return;
			busybuilding = true;

			ShowIndeterminateProgress("Starting to build applications, please wait...");
			ThreadingInterop.PerformVoidFunctionSeperateThread(() =>
			{
				var items = tmpMainListbox.Items;
				List<string> appswithErrors = new List<string>();
				for (int i = 0; i < items.Count; i++)
				{
					BuildApplication buildapp = items[i] as BuildApplication;
					buildapp.LastBuildFeedback = null;
					buildapp.HasErrors = false;
					buildapp.LastBuildResult = null;
				}

				for (int i = 0; i < items.Count; i++)
				//Parallel.For(0, items.Count - 1, (i) =>
				{
					BuildApplication buildapp = items[i] as BuildApplication;
					ShowIndeterminateProgress("Building application: " + buildapp.ApplicationName, true);
					Stopwatch sw = Stopwatch.StartNew();
					string err = buildapp.PerformBuild();
					Logging.LogInfoToFile(string.Format("Duration to build {0} was {1} seconds.", buildapp.ApplicationName, sw.Elapsed.TotalSeconds), Logging.ReportingFrequencies.Daily, "BuildTestSystem", "Benchmarks");
					if (err != null)
						appswithErrors.Add(buildapp.ApplicationName);
				}//);
				if (appswithErrors.Count > 0)
					UserMessages.ShowErrorMessage("Error building the following apps: " + Environment.NewLine +
						string.Join(Environment.NewLine, appswithErrors));
				HideIndeterminateProgress(true);
				busybuilding = false;
			},
			false);
		}

		private void ShowIndeterminateProgress(string message, bool fromSeparateThread = false)
		{
			Action act = delegate
			{
				statusLabel.Content = message;
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
				statusLabel.Content = null;
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
			busybuilding = true;

			var buildapp = GetBuildApplicationFromMenuItem(sender);
			if (null == buildapp) return;

			ShowIndeterminateProgress("Building application " + buildapp.ApplicationName, false);
			ThreadingInterop.PerformOneArgFunctionSeperateThread<BuildApplication>((app) =>
			{
				app.PerformBuild();
				HideIndeterminateProgress(true);
				busybuilding = false;
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
	}

	public class BuildApplication : VSBuildProject, INotifyPropertyChanged
	{
		private string _applicationname;
		public override string ApplicationName { get { return _applicationname; } set { _applicationname = value; OnPropertyChanged("ApplicationName"); } }
		private string _lastbuildfeedback;
		public override string LastBuildFeedback { get { return _lastbuildfeedback ?? ""; } set { _lastbuildfeedback = value; OnPropertyChanged("LastBuildFeedback"); } }
		private bool _haserrors;
		public override bool HasErrors { get { return _haserrors; } set { _haserrors = value; LastBuildResult = !value; OnPropertyChanged("HasErrors"); } }
		private bool? _lastbuildresult;
		public override bool? LastBuildResult { get { return _lastbuildresult; } set { _lastbuildresult = value; OnPropertyChanged("LastBuildResult"); } }

		public BuildApplication(string ApplicationName) : base(ApplicationName) { }

		public event PropertyChangedEventHandler PropertyChanged = new PropertyChangedEventHandler(delegate { });
		public void OnPropertyChanged(string propertyName) { PropertyChanged(this, new PropertyChangedEventArgs(propertyName)); }

		public void OpenInCSharpExpress()
		{
			var csharpPath = RegistryInterop.GetAppPathFromRegistry("VCSExpress.exe");
			if (csharpPath == null)
			{
				UserMessages.ShowErrorMessage("Cannot obtain CSharp Express path from registry.");
				return;
			}
			Process.Start(csharpPath, "\"" + this.SolutionFullpath + "\"");
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
}
