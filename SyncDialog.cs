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
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace KeePassGitHubSecrets
{
	/// <summary>
	/// Confirmation and progress window: pick repositories and secrets, then run
	/// one gh secret set per pair on a worker thread.
	/// </summary>
	public sealed class SyncDialog : Form
	{
		private readonly SecretEntryModel m_model;

		private Label m_lblSummary = null;
		private Label m_lblWarnings = null;
		private Label m_lblStatus = null;
		private CheckedListBox m_clbRepositories = null;
		private CheckedListBox m_clbSecrets = null;
		private ListView m_lvResults = null;
		private ProgressBar m_pbProgress = null;
		private Button m_btnSync = null;
		private Button m_btnClose = null;

		// Touched from the worker thread: keep it volatile.
		private volatile bool m_bBusy = false;

		public SyncDialog(SecretEntryModel model)
		{
			if(model == null) throw new ArgumentNullException("model");

			m_model = model;
			BuildUi();
			FillLists();
		}

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			if(m_bBusy)
			{
				e.Cancel = true;
				m_lblStatus.Text = "Wait for the running sync to finish.";
			}
			base.OnFormClosing(e);
		}

		private void BuildUi()
		{
			SuspendLayout();

			Text = "Sync to GitHub Secrets";
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = false;
			MinimizeBox = false;
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.CenterParent;
			ClientSize = new Size(780, 560);
			AutoScaleDimensions = new SizeF(7F, 15F);
			AutoScaleMode = AutoScaleMode.Font;
			Font = SystemFonts.MessageBoxFont;

			m_lblSummary = new Label();
			m_lblSummary.Location = new Point(12, 12);
			m_lblSummary.Size = new Size(756, 20);

			GroupBox grpRepositories = new GroupBox();
			grpRepositories.Text = "Repositories (" + SecretEntryModel.RepositoryField + ")";
			grpRepositories.Location = new Point(12, 38);
			grpRepositories.Size = new Size(330, 196);

			m_clbRepositories = new CheckedListBox();
			m_clbRepositories.Location = new Point(8, 18);
			m_clbRepositories.Size = new Size(314, 170);
			m_clbRepositories.CheckOnClick = true;
			m_clbRepositories.IntegralHeight = false;
			m_clbRepositories.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
			grpRepositories.Controls.Add(m_clbRepositories);

			GroupBox grpSecrets = new GroupBox();
			grpSecrets.Text = "Secrets (field names, values stay hidden)";
			grpSecrets.Location = new Point(352, 38);
			grpSecrets.Size = new Size(416, 196);

			m_clbSecrets = new CheckedListBox();
			m_clbSecrets.Location = new Point(8, 18);
			m_clbSecrets.Size = new Size(400, 170);
			m_clbSecrets.CheckOnClick = true;
			m_clbSecrets.IntegralHeight = false;
			m_clbSecrets.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
			grpSecrets.Controls.Add(m_clbSecrets);

			m_lblWarnings = new Label();
			m_lblWarnings.Location = new Point(12, 240);
			m_lblWarnings.Size = new Size(756, 34);
			m_lblWarnings.ForeColor = Color.Firebrick;
			m_lblWarnings.AutoEllipsis = true;

			m_lvResults = new ListView();
			m_lvResults.Location = new Point(12, 278);
			m_lvResults.Size = new Size(756, 200);
			m_lvResults.View = View.Details;
			m_lvResults.FullRowSelect = true;
			m_lvResults.GridLines = true;
			m_lvResults.HideSelection = false;
			m_lvResults.MultiSelect = false;
			m_lvResults.Columns.Add("Repository", 200);
			m_lvResults.Columns.Add("Secret", 210);
			m_lvResults.Columns.Add("Result", 70);
			m_lvResults.Columns.Add("Message", 260);

			m_lblStatus = new Label();
			m_lblStatus.Location = new Point(12, 492);
			m_lblStatus.Size = new Size(560, 18);
			m_lblStatus.Text = "Ready.";

			m_pbProgress = new ProgressBar();
			m_pbProgress.Location = new Point(12, 512);
			m_pbProgress.Size = new Size(560, 16);

			m_btnSync = new Button();
			m_btnSync.Text = "Sync";
			m_btnSync.Location = new Point(592, 506);
			m_btnSync.Size = new Size(84, 28);
			m_btnSync.Click += this.OnSyncClick;

			m_btnClose = new Button();
			m_btnClose.Text = "Close";
			m_btnClose.Location = new Point(684, 506);
			m_btnClose.Size = new Size(84, 28);
			m_btnClose.DialogResult = DialogResult.Cancel;

			AcceptButton = m_btnSync;
			CancelButton = m_btnClose;

			Controls.AddRange(new Control[] {
				m_lblSummary, grpRepositories, grpSecrets, m_lblWarnings,
				m_lvResults, m_lblStatus, m_pbProgress, m_btnSync, m_btnClose
			});

			ResumeLayout(false);
			PerformLayout();
		}

		private void FillLists()
		{
			m_lblSummary.Text = "Entry \u201c" + m_model.EntryTitle + "\u201d \u2014 " +
				m_model.Repositories.Count.ToString() + " repositories \u00d7 " +
				m_model.Secrets.Count.ToString() + " secrets";

			foreach(string repository in m_model.Repositories)
				m_clbRepositories.Items.Add(repository, true);

			foreach(SecretField field in m_model.Secrets)
				m_clbSecrets.Items.Add(field, true);

			if(m_model.Warnings.Count > 0)
			{
				List<string> shown = new List<string>();
				for(int i = 0; (i < m_model.Warnings.Count) && (i < 3); ++i)
					shown.Add(m_model.Warnings[i]);
				string text = "Skipped: " + string.Join("  ", shown.ToArray());
				if(m_model.Warnings.Count > shown.Count)
					text += "  (+" + (m_model.Warnings.Count - shown.Count).ToString() + " more)";
				m_lblWarnings.Text = text;
			}

			if((m_model.Repositories.Count == 0) || (m_model.Secrets.Count == 0))
			{
				m_btnSync.Enabled = false;
				if(m_model.Secrets.Count == 0)
					m_lblStatus.Text = "No custom field to push as a secret.";
				else if(m_model.Repositories.Count == 0)
					m_lblStatus.Text = "No repository listed in \"" +
						SecretEntryModel.RepositoryField + "\".";
			}
		}

		private void OnSyncClick(object sender, EventArgs e)
		{
			if(m_bBusy) return;

			List<string> repositories = new List<string>();
			foreach(object item in m_clbRepositories.CheckedItems)
				repositories.Add((string)item);

			List<SecretField> secrets = new List<SecretField>();
			foreach(object item in m_clbSecrets.CheckedItems)
				secrets.Add((SecretField)item);

			if((repositories.Count == 0) || (secrets.Count == 0))
			{
				MessageBox.Show(this, "Select at least one repository and one secret.",
					Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			string ghPath = GhCli.FindExecutable();
			if(ghPath == null)
			{
				MessageBox.Show(this,
					"gh.exe (the GitHub CLI) was not found on PATH or in the Scoop shims.\r\n\r\n" +
					"Install it with \"scoop install gh\" and run \"gh auth login\" once.",
					Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			m_lvResults.Items.Clear();
			m_pbProgress.Minimum = 0;
			m_pbProgress.Maximum = repositories.Count * secrets.Count;
			m_pbProgress.Value = 0;
			m_lblStatus.Text = "Syncing\u2026";
			SetBusy(true);

			Thread worker = new Thread(delegate()
			{
				SyncWorker(repositories, secrets, ghPath);
			});
			worker.IsBackground = true;
			worker.Name = "KeePassGitHubSecrets Sync";
			worker.Start();
		}

		private void SyncWorker(List<string> repositories, List<SecretField> secrets, string ghPath)
		{
			Post(delegate() { m_lblStatus.Text = "Checking gh authentication\u2026"; });

			GhResult auth = GhCli.CheckAuth(ghPath);
			if(!auth.Success)
			{
				string message = auth.Message;
				Post(delegate()
				{
					m_lblStatus.Text = "Not authenticated.";
					SetBusy(false);
					MessageBox.Show(this, message, Text,
						MessageBoxButtons.OK, MessageBoxIcon.Error);
				});
				return;
			}

			int total = repositories.Count * secrets.Count;
			int done = 0;
			int succeeded = 0;
			int failed = 0;

			foreach(string repository in repositories)
			{
				foreach(SecretField field in secrets)
				{
					GhResult result;
					string value = null;
					try
					{
						value = m_model.ReadSecretValue(field);
						result = GhCli.SetSecret(ghPath, repository, field.Name, value);
					}
					catch(Exception ex)
					{
						result = new GhResult(false, ex.Message);
					}
					finally
					{
						value = null; // the string itself cannot be wiped; drop the reference
					}

					++done;
					if(result.Success) ++succeeded;
					else ++failed;

					string repositoryCopy = repository;
					string nameCopy = field.Name;
					string messageCopy = result.Message;
					bool successCopy = result.Success;
					int doneCopy = done;
					int succeededCopy = succeeded;
					int failedCopy = failed;

					Post(delegate()
					{
						ListViewItem item = new ListViewItem(repositoryCopy);
						item.SubItems.Add(nameCopy);
						item.SubItems.Add(successCopy ? "OK" : "FAILED");
						item.SubItems.Add(messageCopy);
						if(!successCopy) item.ForeColor = Color.Firebrick;
						m_lvResults.Items.Add(item);
						item.EnsureVisible();

						int next = m_pbProgress.Value + 1;
						m_pbProgress.Value = (next > m_pbProgress.Maximum)
							? m_pbProgress.Maximum : next;
						m_lblStatus.Text = doneCopy.ToString() + "/" + total.ToString() +
							" \u2014 " + succeededCopy.ToString() + " ok, " +
							failedCopy.ToString() + " failed";
					});
				}
			}

			Post(delegate()
			{
				SetBusy(false);
				m_lblStatus.Text = "Finished: " + succeeded.ToString() + " ok, " +
					failed.ToString() + " failed, " + total.ToString() + " calls.";
			});
		}

		private void SetBusy(bool busy)
		{
			m_bBusy = busy;
			m_btnSync.Enabled = !busy;
			m_btnClose.Enabled = !busy;
			m_clbRepositories.Enabled = !busy;
			m_clbSecrets.Enabled = !busy;
		}

		/// <summary>Marshals a call from the worker thread onto the UI thread.</summary>
		private void Post(MethodInvoker action)
		{
			if(IsDisposed) return;
			try
			{
				BeginInvoke(action);
			}
			catch(ObjectDisposedException) { }
			catch(InvalidOperationException) { }
		}
	}
}
