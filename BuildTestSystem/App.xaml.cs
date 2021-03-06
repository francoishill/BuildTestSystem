﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using SharedClasses;
using System.Threading;
using System.IO;
using System.Net;
using System.Diagnostics;

namespace BuildTestSystem
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application
	{
		private enum CommandlineTasks
		{
			ListTasks,//Get the available tasks that can be performed, writes it to the Console.Out
			GetList,//Sends a lists via Console.Out
			Build,//Build (on server) and writes the output to Console.Out / Console.Error
			PublishSetup,//Publishes the application (from the server) and writes the output to Console.Out / Console.Error
			IsInstalledOnServer,//Check whether app is installed on server, write it to Console.Out
			SubversionStatus,//Check the Subversioning status (on the server), writes it to Console.Out / Console.Error
			SubversionUpdate,//Updates from Subversion (on the server), writes the output to Console.Out / Console.Error
			SubversionCommit,//Commits to Subversion (on the server), writes the output to Console.Out / Console.Out
			DownloadTempSetup,//When a setup was created by PublishSetup
		};

		private bool VerifyCommandlineArgsForTask(CommandlineTasks task, List<string> args, out BuildApplication buildapp)
		{
			switch (task)
			{
				case CommandlineTasks.Build:
				case CommandlineTasks.PublishSetup:
				case CommandlineTasks.IsInstalledOnServer:
				case CommandlineTasks.SubversionStatus:
				//case CommandlineTasks.SubversionCommit:
				case CommandlineTasks.SubversionUpdate:
				case CommandlineTasks.SubversionCommit:
				case CommandlineTasks.DownloadTempSetup:
					if (args.Count < 2)//We need the command and the ApplicationName
					{
						WriteError("Cannot use command '" + task + "' without ApplicationName commandline argument");
						buildapp = null;
						return false;
					}
					else
					{
						buildapp = new BuildApplication(args[1]);
						if (buildapp != null && buildapp.SolutionFullpath != null)//Could not find the directory
						{
							if (task != CommandlineTasks.DownloadTempSetup)
								return true;
							else
							{
								if (args.Count < 3)//We need to have the setup name too
								{
									WriteError("Cannot use command '" + task + "' without SetupFilename commandline argument as 3rd argument");
									buildapp = null;
									return false;
								}
								else
									return true;
							}
						}
						else
							return false;
					}
			}
			buildapp = null;
			return true;//If we did not find an error
		}

		protected override void OnStartup(StartupEventArgs e)
		{
			//SharedClasses.ResourceUsageTracker.ShowResourceUsageChart();
			//return;

			//SharedClasses.HighResourceUsageWindow.ShowHighResourceUsageWindowReturnResult(
			//	"Message",
			//	"Current time is " + DateTime.Now.ToString("HH:mm:ss"));

			//Console.Out.WriteLine(1);
			//Console.Out.Flush();
			//Console.Out.WriteLine("HAllo");
			//Console.Out.Flush();
			//Console.Out.Close();
			//Environment.Exit(0);

			/*AppDomain.CurrentDomain.UnhandledException += (snder, exc) =>
			{
				Exception exception = (Exception)exc.ExceptionObject;
				//ShowError("Exception" + (exc.IsTerminating ? ", application will now exit" : "") + ":"
				//    + exception.Message + Environment.NewLine + exception.StackTrace);
				if (Environment.GetCommandLineArgs().Length > 1)//The first is this exe path, this means we have an argument
				{
					WriteError("Unhandled exception: " + exception.Message
						+ Environment.NewLine + "Stack trace: " + exception.StackTrace);
				}
				UserMessages.ShowErrorMessage("Unhandled exception: " + exception.Message
						+ Environment.NewLine + "Stack trace: " + exception.StackTrace);
			};*/

			SharedClasses.AutoUpdating.CheckForUpdates_ExceptionHandler();
			//SharedClasses.AutoUpdating.CheckForUpdates(null, null, true);

			List<string> args = Environment.GetCommandLineArgs().ToList();
			args.RemoveAt(0);

			//WriteOutput("Test ouput 1");
			//WriteError("Test error 1" + Environment.NewLine + "Same error new line");
			//WriteOutput("Test ouput 1");
			//Environment.Exit(0);

			//args = new List<string>()
			//{
			//    "SubversionStatus",//"build",
			//    "MonitorSystem"
			//};

			if (args.Count == 0)//No commandline args, running normally
			{
				base.OnStartup(e);
				MainWindow mw = new MainWindow();
				mw.ShowDialog();
				return;
			}

			try
			{
				BuildApplication.ActionOnFeedbackMessageReceived =
				(app, message, msgtype) =>
				{
					if (message != null)
					{
						if (msgtype == FeedbackMessageTypes.Error)
							WriteError(message);
						else if (msgtype == FeedbackMessageTypes.Warning)
							WriteOutput("WARN: " + message);
						else
							WriteOutput(message);
					}
				};

				BuildApplication.ActionOnProgressPercentageChanged =
					(app, newprogress) =>
					{
						if (newprogress.HasValue)
							WriteOutput("Progress: " + newprogress + "%");
					};

				string arg1task = args[0];
				
				string arg2appname = args.Count > 1 ? args[1] : null;//If we do not have applicationName
				
				string arg3setupfilename = args.Count > 2 ? args[2] : null;//If we do not have a setup filename (only used for DownloadTempSetup)
				string arg3commitMsg = args.Count > 2 ? args[2] : null;//Yes, also the third param, same as for arg3setupfilename. If we do a subversionCommit, this will be the message
				bool arg3fullstatus = args.Count > 2 ? args[2].Equals("fullstatus", StringComparison.InvariantCultureIgnoreCase) : false;

				CommandlineTasks task;
				if (!Enum.TryParse<CommandlineTasks>(arg1task, true, out task))
				{
					WriteError("Cannot parse CommandlineTasks from string: " + arg1task);
					return;
				}

				BuildApplication buildapp;//Will only be used if this task wants an applicationName
				if (!VerifyCommandlineArgsForTask(task, args, out buildapp))
					return;

				int tmpOutExitCode;
				switch (task)
				{
					case CommandlineTasks.ListTasks:
						List<string> tasklist = new List<string>();
						var tasks = Enum.GetValues(typeof(CommandlineTasks));
						foreach (CommandlineTasks t in tasks)
							if (t != CommandlineTasks.GetList
								&& t != CommandlineTasks.ListTasks
								&& t != CommandlineTasks.DownloadTempSetup)
								tasklist.Add(t.ToString());
						WriteOutput(string.Join(Environment.NewLine, tasklist));
						tasklist.Clear(); tasklist = null;
						break;
					case CommandlineTasks.GetList:
						var applist = SettingsSimple.BuildTestSystemSettings.Instance.ListOfApplicationsToBuild;
						WriteOutput(string.Join(Environment.NewLine, applist));
						break;
					case CommandlineTasks.Build:
						List<string> tmpl;
						bool buildSuccess = buildapp.PerformBuild(out tmpl);
						if (buildSuccess)
							WriteOutput("Successfully built: " + buildapp.ApplicationName);
						break;
					case CommandlineTasks.PublishSetup:
						WriteOutput("Started publishing " + arg2appname + "...");

						ClearAllGuidFilesOlderThanAday(
							(filenameOnly) =>
							{
								Guid tmpG;
								if (!Guid.TryParse(Path.GetFileNameWithoutExtension(filenameOnly), out tmpG))
									return false;
								else if (!filenameOnly.EndsWith(".exe", StringComparison.InvariantCultureIgnoreCase))
									return false;
								else
									return true;
							});

						Guid newGuid = Guid.NewGuid();
						string setupFilename = newGuid.ToString() + ".exe";
						bool publishSuccess = buildapp.PerformPublish(
							false,
							false,
							false,
							true,
							setupFilename);
						if (publishSuccess)
							WriteSuccessUrl(
								"downloadtempsetup" + "/" + buildapp.ApplicationName + "/" + setupFilename,
								prefix: "Url for temp setup file: ");
						break;
					case CommandlineTasks.IsInstalledOnServer:
						WriteOutput(buildapp.IsInstalled.ToString());
						break;
					case CommandlineTasks.SubversionStatus:
						if (!arg3fullstatus)
						{
							string changesText;
							ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
							WriteOutput("Starting to check for subversion changes");
							if (TortoiseProcInterop.CheckFolderSubversionChanges(Path.GetDirectoryName(buildapp.SolutionFullpath), out changesText))
							{
								buildapp.AppendCurrentStatusText(changesText);
								bool hasLocalChanges = TortoiseProcInterop.Subversion_HasLocalChanges(changesText);
								bool hasRemoteChanges = TortoiseProcInterop.Subversion_HasRemoteChanges(changesText);
								WriteOutput(changesText
									+ (hasLocalChanges ? "[HASLOCALSUBVERSIONCHANGES]" : "")
									+ (hasRemoteChanges ? "[HASREMOTESUBVERSIONCHANGES]" : ""));//To say we had changes
							}
							else//Successfully ran, but no subversion changes
							{
								buildapp.CurrentStatus = BuildApplication.StatusTypes.Success;
								WriteOutput("No subversion changes.");//DO NOT CHANGE (same in codeigniter app, application\controllers\buildtestsystem.php)
							}
						}
						else
						{
							string diffText;
							ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
							WriteOutput("Starting to check for subversion changes");
							if (TortoiseProcInterop.CheckFolderSubversionDiff(Path.GetDirectoryName(buildapp.SolutionFullpath), out diffText))
							{
								buildapp.AppendCurrentStatusText(diffText);
								var separateFileBlocks = diffText.Split(new string[] { "Index:" }, StringSplitOptions.RemoveEmptyEntries);
								string finalText = "";
								foreach (var block in separateFileBlocks)//Write each block separately so php can read while we send it
								{
									int indexFirstNewlineChar = block.IndexOf('\n');
									string firstLineOfBlock = block.Substring(0, indexFirstNewlineChar).Trim();
									WriteOutput(finalText +=
										"[DIFFBLOCKSTART]"
										+ "[BLOCKFIRSTLINESTART]" + firstLineOfBlock + "[BLOCKFIRSTLINEEND]"
										+ block.Substring(indexFirstNewlineChar)
										+ "[DIFFBLOCKEND]");
								}
							}
							else//Successfully ran, but no subversion changes
							{
								buildapp.CurrentStatus = BuildApplication.StatusTypes.Success;
								//WriteOutput("No subversion diffs.");
							}
						}
						break;
					//case CommandlineTasks.SubversionCommit:
					//    WriteOutput("Committing subversion " + arg2appname + "...");
					//    break;
					case CommandlineTasks.SubversionUpdate:
						WriteOutput("Updating subversion " + arg2appname + "...");
						ProcessesInterop.StartAndWaitProcessRedirectOutput(
							new ProcessStartInfo(
								TortoiseProcInterop.cSvnPath,
								"update " + TortoiseProcInterop.GetExtraSvnParams() + " \"" + Path.GetDirectoryName(buildapp.SolutionFullpath) + "\""),
							(sn, o) => { WriteOutput(o); },
							(sn, er) => { WriteError(er); },
							out tmpOutExitCode);
						break;
					case CommandlineTasks.SubversionCommit:
						if (arg3commitMsg == null)
						{
							WriteError("Commit message cannot be NULL.");
							return;
						}

						string commitMsg = "";
						if (arg3commitMsg.Trim() != "___")//This was posted via buildtestsystem.js, and it means "no commit message"
						{
							/*const string forwardSlashToken = "_FWSLASH_";//DO NOT CHANGE (same in codeigniter app, js\buildtestsystem.js): Cannot encode a forward slash in javascript the normal way, use own token
							const string minusToken = "_MINUS_";//DO NOT CHANGE (same in codeigniter app, js\buildtestsystem.js): Cannot encode a minus sign in javascript the normal way, use own token
							const string commaToken = "_COMMA_";//DO NOT CHANGE (same in codeigniter app, js\buildtestsystem.js): Cannot encode a comma in javascript the normal way, use own token
							const string hashToken = "_HASH_";//DO NOT CHANGE (same in codeigniter app, js\buildtestsystem.js): Cannot encode a hash in javascript the normal way, use own token
							commitMsg =
								Uri.UnescapeDataString(arg3commitMsg)
								.Replace(forwardSlashToken, "/")
								.Replace(minusToken, "-")
								.Replace(commaToken, ",")
								.Replace(hashToken, "#");*/
							commitMsg = EncodeAndDecodeInterop.DecodeStringHex(arg3commitMsg.ToUpper());
						}
						WriteOutput("Commiting, message = " + commitMsg);
						ProcessesInterop.StartAndWaitProcessRedirectOutput(
							new ProcessStartInfo(
								TortoiseProcInterop.cSvnPath,
								"commit -m\"" + commitMsg + "\" " + TortoiseProcInterop.GetExtraSvnParams() + " \"" + Path.GetDirectoryName(buildapp.SolutionFullpath) + "\""),
							(sn, o) => { WriteOutput(o); },
							(sn, er) => { WriteError(er); },
							out tmpOutExitCode);
						break;
					case CommandlineTasks.DownloadTempSetup:
						string fullServerPathToSetup =
							PublishInterop.GetNsisExportsPath(PublishInterop.cTempWebfolderName)
							+ "\\" + arg3setupfilename;
						//WriteOutput("Starting to send setup: " + fullServerPathToSetup);

						//fullServerPathToSetup = fullServerPathToSetup.Replace(
						//    CalledFromService.Environment_GetFolderPath(Environment.SpecialFolder.LocalApplicationData).TrimEnd('\\'),
						//    "%LocalAppData%");
						WriteOutput(
							"[DOWNLOADTEMPSETUPFULLPATHSTART]"
							+ fullServerPathToSetup
							+ "[DOWNLOADTEMPSETUPFULLPATHEND]");

						//    + PublishInterop.GetNsisExportsPath(PublishInterop.cTempWebfolderName)
						//    + "\\" + arg3setupfilename);
						//File.OpenText
						//using (var fs = new FileStream(fullServerPathToSetup, FileMode.Open))
						//using (var co = Console.Out)
						//{

						//}
						break;
					default:
						break;
				}
			}
			finally
			{
				Console.Out.Close();
				Console.Error.Close();
				Application.Current.Shutdown(0);
			}
		}

		private void ClearAllGuidFilesOlderThanAday(Predicate<string> shouldDeleteCheckFilenameOnly)
		{
			string tempWebFolder =
				PublishInterop.GetNsisExportsPath(PublishInterop.cTempWebfolderName);
			if (!Directory.Exists(tempWebFolder))
				return;

			DateTime now = DateTime.Now;
			foreach (var file in Directory.GetFiles(tempWebFolder))
				if (
					now.Subtract(File.GetLastWriteTime(file)).TotalDays >= 1
					&& shouldDeleteCheckFilenameOnly(Path.GetFileName(file)))
				{
					try { File.Delete(file); }
					catch (Exception exc)
					{
						UserMessages.ShowErrorMessage("Could not delete file, message: " + exc.Message);
					}
				}
		}

		private void WriteOutput(string ouputText, bool flush = true)
		{
			Console.Out.WriteLine(ouputText);
			if (flush)
				Console.Out.Flush();
		}

		private void WriteError(string errorText, bool flush = true)
		{
			Console.Out.WriteLine("[ERRORSTART]" + errorText + "[ERROREND]");
			if (flush)
				Console.Out.Flush();
		}

		private void WriteSuccessUrl(string successUrl, bool flush = true, string prefix = "")
		{
			WriteOutput(prefix + "[SUCCESSURLSTART]" + successUrl + "[SUCCESSURLEND]");
		}
	}
}
