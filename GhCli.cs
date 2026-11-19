/*
  KeePassGitHubSecrets - push entry custom fields to GitHub Actions secrets
  Copyright (C) 2026 shijianjs

  This program is free software; you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation; either version 2 of the License, or
  (at your option) any later version. See LICENSE for the full text.
*/

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace SamplePlugin
{
	/// <summary>Outcome of one gh invocation.</summary>
	public sealed class GhResult
	{
		public readonly bool Success;
		public readonly string Message;

		public GhResult(bool success, string message)
		{
			Success = success;
			Message = (message == null) ? string.Empty : message;
		}
	}

	/// <summary>
	/// Thin wrapper around the locally installed and already authenticated gh CLI.
	/// The plugin never handles a token: gh uses its own credential store.
	/// </summary>
	public static class GhCli
	{
		private const int TimeoutMs = 60000;
		private const string ExecutableName = "gh.exe";

		/// <summary>Finds gh.exe in PATH, then in the common Scoop shim folders.</summary>
		public static string FindExecutable()
		{
			foreach(string dir in SplitPath(Environment.GetEnvironmentVariable("PATH")))
			{
				string candidate = Path.Combine(dir, ExecutableName);
				if(File.Exists(candidate)) return candidate;
			}

			string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			string[] roots = new string[] {
				Environment.GetEnvironmentVariable("SCOOP"),
				Environment.GetEnvironmentVariable("SCOOP_GLOBAL"),
				Path.Combine(userProfile, "scoop"),
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "scoop")
			};

			foreach(string root in roots)
			{
				if((root == null) || (root.Length == 0)) continue;
				string candidate = Path.Combine(root, "shims", ExecutableName);
				if(File.Exists(candidate)) return candidate;
			}

			return null;
		}

		/// <summary>Checks that gh is logged in. Runs gh auth status once per sync.</summary>
		public static GhResult CheckAuth(string executablePath)
		{
			if(string.IsNullOrEmpty(executablePath))
				return new GhResult(false, "gh.exe was not found on PATH or in the Scoop shims.");

			GhResult result = Run(executablePath, "auth status", null);
			if(result.Success) return result;

			return new GhResult(false,
				"gh is not authenticated.\r\n\r\n" +
				"Run \"gh auth login\" in a terminal, then try again.\r\n\r\n" +
				result.Message);
		}

		/// <summary>Writes one secret. The value travels on stdin only, so it never
		/// shows up in the process command line.</summary>
		public static GhResult SetSecret(string executablePath, string repository,
			string name, string value)
		{
			if(string.IsNullOrEmpty(executablePath))
				return new GhResult(false, "gh.exe was not found on PATH or in the Scoop shims.");
			if(!IsValidRepository(repository))
				return new GhResult(false, "Invalid repository: " + repository);
			if(!IsValidSecretName(name))
				return new GhResult(false, "Invalid secret name: " + name);
			if((value == null) || (value.Length == 0))
				return new GhResult(false, "The field is empty, skipped.");

			return Run(executablePath,
				"secret set " + name + " --repo " + repository, value);
		}

		public static bool IsValidRepository(string repository)
		{
			if(string.IsNullOrEmpty(repository)) return false;
			int slash = repository.IndexOf('/');
			if(slash <= 0 || slash != repository.LastIndexOf('/')) return false;
			return repository.IndexOf(' ') < 0;
		}

		public static bool IsValidSecretName(string name)
		{
			if(string.IsNullOrEmpty(name)) return false;
			if(!(char.IsLetter(name[0]) || (name[0] == '_'))) return false;
			foreach(char c in name)
			{
				if(!(char.IsLetterOrDigit(c) || (c == '_'))) return false;
			}
			return true;
		}

		private static string[] SplitPath(string pathValue)
		{
			if(string.IsNullOrEmpty(pathValue)) return new string[0];
			return pathValue.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
		}

		private static GhResult Run(string executablePath, string arguments, string stdinValue)
		{
			Process process = null;
			string stdout = string.Empty;
			string stderr = string.Empty;

			try
			{
				ProcessStartInfo startInfo = new ProcessStartInfo();
				startInfo.FileName = executablePath;
				startInfo.Arguments = arguments;
				startInfo.UseShellExecute = false;
				startInfo.CreateNoWindow = true;
				startInfo.RedirectStandardInput = true;
				startInfo.RedirectStandardOutput = true;
				startInfo.RedirectStandardError = true;
				startInfo.StandardOutputEncoding = Encoding.UTF8;
				startInfo.StandardErrorEncoding = Encoding.UTF8;

				process = new Process();
				process.StartInfo = startInfo;
				if(!process.Start())
					return new GhResult(false, "Could not start " + executablePath);

				// stdout/stderr are drained on their own threads so neither pipe can
				// block the child while we are writing stdin.
				Thread stdoutThread = new Thread(delegate()
				{
					try { stdout = process.StandardOutput.ReadToEnd(); } catch(Exception) { }
				});
				Thread stderrThread = new Thread(delegate()
				{
					try { stderr = process.StandardError.ReadToEnd(); } catch(Exception) { }
				});
				stdoutThread.IsBackground = true;
				stderrThread.IsBackground = true;
				stdoutThread.Start();
				stderrThread.Start();

				try
				{
					if(stdinValue != null)
					{
						using(StreamWriter writer = new StreamWriter(
							process.StandardInput.BaseStream, new UTF8Encoding(false)))
						{
							writer.Write(stdinValue);
							writer.Flush();
						}
					}
					else
					{
						process.StandardInput.Close();
					}
				}
				catch(Exception) { } // gh exited before reading stdin; reported below

				if(!process.WaitForExit(TimeoutMs))
				{
					try { process.Kill(); } catch(Exception) { }
					return new GhResult(false,
						"gh did not finish within " + (TimeoutMs / 1000).ToString() + " s.");
				}

				stdoutThread.Join(2000);
				stderrThread.Join(2000);

				if(process.ExitCode == 0) return new GhResult(true, FirstLine(stdout));

				return new GhResult(false,
					FirstLine((stderr.Length > 0) ? stderr : stdout));
			}
			catch(Exception ex)
			{
				return new GhResult(false, ex.Message);
			}
			finally
			{
				if(process != null)
				{
					try { process.Dispose(); } catch(Exception) { }
				}
			}
		}

		private static string FirstLine(string text)
		{
			if(string.IsNullOrEmpty(text)) return string.Empty;

			foreach(string line in text.Split(new char[] { '\r', '\n' }))
			{
				string trimmed = line.Trim();
				if(trimmed.Length == 0) continue;
				return (trimmed.Length > 300) ? (trimmed.Substring(0, 300) + "…") : trimmed;
			}
			return string.Empty;
		}
	}
}
