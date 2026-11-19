/*
  KeePassGitHubSecrets - push entry custom fields to GitHub Actions secrets
  Copyright (C) 2026 shijianjs

  Based on SamplePlugin, an example KeePass plugin
  Copyright (C) 2003-2019 Dominik Reichl <dominik.reichl@t-online.de>

  This program is free software; you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation; either version 2 of the License, or
  (at your option) any later version. See LICENSE for the full text.
*/

using System;
using System.Windows.Forms;

using KeePass.Plugins;

using KeePassLib;

// The namespace name must be the same as the file name of the plugin
// without its extension, i.e. the DLL has to be KeePassGitHubSecrets.dll.
namespace KeePassGitHubSecrets
{
	// Namespace name 'KeePassGitHubSecrets' + 'Ext' = 'KeePassGitHubSecretsExt'
	public sealed class KeePassGitHubSecretsExt : Plugin
	{
		private const string MenuText = "Sync to GitHub Secrets\u2026";

		private IPluginHost m_host = null;

		public override bool Initialize(IPluginHost host)
		{
			if(host == null) return false; // Fail; we need the host
			m_host = host;
			return true;
		}

		public override void Terminate()
		{
			m_host = null;
		}

		public override ToolStripMenuItem GetMenuItem(PluginMenuType t)
		{
			// One command, on entries: the entry is the unit of synchronisation.
			if(t != PluginMenuType.Entry) return null;

			ToolStripMenuItem tsmi = new ToolStripMenuItem(MenuText);
			tsmi.Click += this.OnSyncSecrets;
			return tsmi;
		}

		private void OnSyncSecrets(object sender, EventArgs e)
		{
			if(m_host == null) return;

			PwDatabase pd = m_host.Database;
			if((pd == null) || !pd.IsOpen) return;

			PwEntry[] selected = m_host.MainWindow.GetSelectedEntries();
			if((selected == null) || (selected.Length != 1))
			{
				MessageBox.Show(m_host.MainWindow,
					"Select exactly one entry: the entry holds the repository list " +
					"and the secrets to push.",
					MenuText, MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			PwEntry entry = selected[0];
			SecretEntryModel model = SecretEntryModel.Analyze(entry);

			using(SyncDialog dialog = new SyncDialog(model))
			{
				dialog.ShowDialog(m_host.MainWindow);
			}
		}
	}
}
