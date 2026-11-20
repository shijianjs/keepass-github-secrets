/*
  KeePassGitHubSecrets - push entry custom fields to GitHub Actions secrets
  Copyright (C) 2026 shijianjs

  This program is free software; you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation; either version 2 of the License, or
  (at your option) any later version. See LICENSE for the full text.
*/

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using KeePassLib;
using KeePassLib.Security;

namespace KeePassGitHubSecrets
{
	/// <summary>One custom field of the entry, i.e. one GitHub secret.</summary>
	public sealed class SecretField
	{
		public readonly string Name;
		public readonly int ValueLength;
		public readonly bool IsProtected;

		internal SecretField(string name, int valueLength, bool isProtected)
		{
			Name = name;
			ValueLength = valueLength;
			IsProtected = isProtected;
		}

		public override string ToString()
		{
			return Name + " (" + ValueLength.ToString() + " chars)";
		}
	}

	/// <summary>
	/// Reads the conventions of one entry: the GitHubRepos field lists the target
	/// repositories, every other custom field is a secret named after the field.
	/// Pure logic, no UI; values are only measured here, never kept.
	/// </summary>
	public sealed class SecretEntryModel
	{
		public const string RepositoryField = "GitHubRepos";

		private const string SshPrefix = "ssh://git@github.com/";
		private const string GitAtPrefix = "git@github.com:";
		private const string HostPrefix = "github.com/";

		private static readonly Regex s_reRepository = new Regex(
			@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.Compiled);
		private static readonly Regex s_reSecretName = new Regex(
			@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

		// Standard KeePass fields plus the tag field and the repository list itself.
		private static readonly string[] s_ignoredFields = new string[] {
			PwDefs.TitleField, PwDefs.UserNameField, PwDefs.PasswordField,
			PwDefs.UrlField, PwDefs.NotesField, "Tags", RepositoryField
		};

		private readonly PwEntry m_entry;

		public readonly List<string> Repositories = new List<string>();
		public readonly List<SecretField> Secrets = new List<SecretField>();
		public readonly List<string> Warnings = new List<string>();

		public PwEntry Entry { get { return m_entry; } }

		public string EntryTitle
		{
			get
		{
				string s = m_entry.Strings.ReadSafe(PwDefs.TitleField);
				return (s.Length > 0) ? s : "(untitled)";
			}
		}

		private SecretEntryModel(PwEntry entry)
		{
			m_entry = entry;
		}

		public static SecretEntryModel Analyze(PwEntry entry)
		{
			if(entry == null) throw new ArgumentNullException("entry");

			SecretEntryModel model = new SecretEntryModel(entry);
			model.ReadRepositories();
			model.ReadSecrets();
			return model;
		}

		/// <summary>Reads the secret value. Callers must drop the returned string
		/// as soon as it has been handed over.</summary>
		public string ReadSecretValue(SecretField field)
		{
			if(field == null) return null;
			ProtectedString ps = m_entry.Strings.Get(field.Name);
			return (ps == null) ? null : ps.ReadString();
		}

		private void ReadRepositories()
		{
			string text = null;
			foreach(string key in m_entry.Strings.GetKeys())
			{
				if(string.Equals(key, RepositoryField, StringComparison.OrdinalIgnoreCase))
				{
					text = m_entry.Strings.ReadSafe(key);
					break;
				}
			}

			if((text == null) || (text.Trim().Length == 0))
			{
				Warnings.Add("This entry has no \"" + RepositoryField +
					"\" field, so there is no repository to sync to.");
				return;
			}

			int lineNumber = 0;
			foreach(string rawLine in text.Split(new char[] { '\r', '\n' }))
			{
				++lineNumber;
				string line = NormalizeRepository(rawLine);
				if(line.Length == 0) continue; // blank line or comment

				if(!s_reRepository.IsMatch(line))
				{
					Warnings.Add("Line " + lineNumber.ToString() + " is not owner/repo: " +
						Shorten(rawLine.Trim(), 60));
					continue;
				}
				if(Repositories.Contains(line)) continue; // duplicate line

				Repositories.Add(line);
			}
		}

		private void ReadSecrets()
		{
			foreach(string key in m_entry.Strings.GetKeys())
			{
				if(IsIgnoredField(key)) continue;

				if(!s_reSecretName.IsMatch(key))
				{
					Warnings.Add("Field \"" + key +
						"\" is not a valid GitHub secret name, skipped.");
					continue;
				}

				ProtectedString ps = m_entry.Strings.Get(key);
				if(ps == null) continue;

				Secrets.Add(new SecretField(key, ps.ReadString().Length, ps.IsProtected));
			}
		}

		private static bool IsIgnoredField(string key)
		{
			foreach(string ignored in s_ignoredFields)
			{
				if(string.Equals(key, ignored, StringComparison.OrdinalIgnoreCase))
					return true;
			}
			return false;
		}

		/// <summary>Turns a line of the repository field into owner/repo.
		/// Returns an empty string for blank lines and # comments.</summary>
		private static string NormalizeRepository(string raw)
		{
			if(raw == null) return string.Empty;

			string s = raw.Trim();
			if(s.Length == 0) return string.Empty;
			if(s.StartsWith("#", StringComparison.Ordinal)) return string.Empty;

			if(s.StartsWith(SshPrefix, StringComparison.OrdinalIgnoreCase))
				s = s.Substring(SshPrefix.Length);
			else if(s.StartsWith(GitAtPrefix, StringComparison.OrdinalIgnoreCase))
				s = s.Substring(GitAtPrefix.Length);
			else
			{
				if(s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
					s = s.Substring(8);
				else if(s.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
					s = s.Substring(7);
			}

			if(s.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
				s = s.Substring(4);
			if(s.StartsWith(HostPrefix, StringComparison.OrdinalIgnoreCase))
				s = s.Substring(HostPrefix.Length);

			s = s.TrimEnd('/');
			if(s.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
				s = s.Substring(0, s.Length - 4);
			return s;
		}

		private static string Shorten(string s, int maxLength)
		{
			if((s == null) || (s.Length <= maxLength)) return s;
			return s.Substring(0, maxLength) + "…";
		}
	}
}
