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
using System.Globalization;

using KeePassLib.Translation;

namespace KeePassGitHubSecrets
{
	/// <summary>
	/// Minimal translation layer. English text doubles as the key; Simplified
	/// Chinese is the only other language. Which one is used follows KeePass'
	/// own UI language (KeePass.Program.Translation), with the Windows display
	/// language as a fallback when KeePass itself runs in English.
	/// </summary>
	public static class Str
	{
		private static readonly bool s_bChinese = DetectChinese();

		private static readonly Dictionary<string, string> s_zh =
			new Dictionary<string, string>(StringComparer.Ordinal)
		{
			// plugin entry + messages
			{ "Sync to GitHub Secrets\u2026", "同步到 GitHub Secrets…" },
			{ "Sync to GitHub Secrets", "同步到 GitHub Secrets" },
			{ "Select exactly one entry: the entry holds the repository list " +
				"and the secrets to push.",
				"请只选中一条条目：仓库列表和要推送的密钥都在这一条里。" },

			// dialog chrome
			{ "Repositories ({0})", "仓库（{0}）" },
			{ "Secrets (field names, values stay hidden)", "密钥（只显示字段名，值不显示）" },
			{ "Entry \u201c{0}\u201d \u2014 {1} repositories \u00d7 {2} secrets",
				"条目“{0}”——{1} 个仓库 × {2} 个密钥" },
			{ "Skipped: {0}", "已跳过：{0}" },
			{ " (+{0} more)", "（另有 {0} 条）" },
			{ "Repository", "仓库" },
			{ "Secret", "密钥" },
			{ "Result", "结果" },
			{ "Message", "消息" },
			{ "Ready.", "就绪。" },
			{ "Wait for the running sync to finish.", "同步还在进行，请等它结束。" },
			{ "No custom field to push as a secret.", "没有可推送为密钥的自定义字段。" },
			{ "No repository listed in \"{0}\".", "字段“{0}”里没有列出仓库。" },
			{ "Select at least one repository and one secret.",
				"至少勾选一个仓库和一个密钥。" },

			// sync run
			{ "Syncing\u2026", "正在同步…" },
			{ "Checking gh authentication\u2026", "正在检查 gh 认证…" },
			{ "Not authenticated.", "未登录。" },
			{ "{0}/{1} \u2014 {2} ok, {3} failed", "{0}/{1} — 成功 {2}，失败 {3}" },
			{ "Finished: {0} ok, {1} failed, {2} calls.", "结束：成功 {0}，失败 {1}，共 {2} 次调用。" },
			{ "OK", "OK" },
			{ "FAILED", "失败" },
			{ "Sync", "同步" },
			{ "Close", "关闭" },

			// gh missing / auth
			{ "gh.exe (the GitHub CLI) was not found on PATH or in the Scoop shims.\r\n\r\n" +
				"Install it with \"scoop install gh\" and run \"gh auth login\" once.",
				"PATH 和 Scoop 的 shims 里都没找到 gh.exe（GitHub CLI）。\r\n\r\n" +
				"先执行 \"scoop install gh\" 安装，再执行一次 \"gh auth login\"。" },
			{ "gh.exe was not found on PATH or in the Scoop shims.",
				"PATH 和 Scoop 的 shims 里都没找到 gh.exe。" },
			{ "gh is not authenticated.\r\n\r\n" +
				"Run \"gh auth login\" in a terminal, then try again.\r\n\r\n{0}",
				"gh 未登录。\r\n\r\n请先在终端执行 \"gh auth login\"，再重试。\r\n\r\n{0}" },

			// validation
			{ "Invalid repository: {0}", "仓库名无效：{0}" },
			{ "Invalid secret name: {0}", "密钥名无效：{0}" },
			{ "The field is empty, skipped.", "字段为空，已跳过。" },
			{ "Could not start {0}", "无法启动 {0}" },
			{ "gh did not finish within {0} s.", "gh 在 {0} 秒内没有结束。" },

			// entry model warnings
			{ "This entry has no \"{0}\" field, so there is no repository to sync to.",
				"这条条目没有“{0}”字段，没有可同步的仓库。" },
			{ "Line {0} is not owner/repo: {1}", "第 {0} 行不是 owner/repo 格式：{1}" },
			{ "Field \"{0}\" is not a valid GitHub secret name, skipped.",
				"字段“{0}”不是合法的 GitHub 密钥名，已跳过。" },

			// secret list entries
			{ "{0} ({1} chars)", "{0}（{1} 个字符）" }
		};

		public static bool IsChinese
		{
			get { return s_bChinese; }
		}

		/// <summary>Translates one literal; unknown text falls back to English.</summary>
		public static string T(string english)
		{
			if(!s_bChinese || string.IsNullOrEmpty(english)) return english;

			string translated;
			if(s_zh.TryGetValue(english, out translated)) return translated;
			return english;
		}

		/// <summary>Translates a format string, then fills in the arguments.</summary>
		public static string F(string english, params object[] args)
		{
			return string.Format(CultureInfo.CurrentCulture, T(english), args);
		}

		private static bool DetectChinese()
		{
			try
			{
				KPTranslation translation = KeePass.Program.Translation;
				if(translation != null)
				{
					KPTranslationProperties props = translation.Properties;
					if(props != null)
					{
						// "zh-CN", "zh-TW", ... ; English translation reports "en".
						string code = props.Iso6391Code;
						if(!string.IsNullOrEmpty(code))
							return code.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

						// No code: the native name of a Chinese file contains 中文/漢語.
						string native = props.NameNative;
						if(!string.IsNullOrEmpty(native))
							return native.IndexOf('\u4e2d') >= 0; // 中
					}

					// KeePass has a translation loaded but it tells us nothing:
					// treat it as English rather than guessing.
					return false;
				}
			}
			catch(Exception) { }

			// KeePass runs in English (no translation file): follow Windows.
			try
			{
				return CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "zh";
			}
			catch(Exception) { }

			return false;
		}
	}
}
