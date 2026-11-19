/*
  KeePassGitHubSecrets - push entry custom fields to GitHub Actions secrets
  Copyright (C) 2026 shijianjs

  Based on SamplePlugin, an example KeePass plugin
  Copyright (C) 2003-2019 Dominik Reichl <dominik.reichl@t-online.de>

  This program is free software; you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation; either version 2 of the License, or
  (at your option) any later version.

  This program is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with this program; if not, write to the Free Software
  Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
*/

using System.Reflection;
using System.Runtime.InteropServices;

// General assembly properties
[assembly: AssemblyTitle("KeePassGitHubSecrets")]
[assembly: AssemblyDescription("Synchronises secrets stored as entry custom fields to GitHub Actions secrets via the gh CLI.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("shijianjs")]
[assembly: AssemblyProduct("KeePassGitHubSecrets")]
[assembly: AssemblyCopyright("Copyright © 2026 shijianjs; based on SamplePlugin © 2003-2019 Dominik Reichl")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// COM settings
[assembly: ComVisible(false)]

// Assembly GUID
[assembly: Guid("6f0a1c53-2b7a-4d9e-9a51-0c5c8f4b1d21")]

// Assembly version information
[assembly: AssemblyVersion("0.1.0.*")]
[assembly: AssemblyFileVersion("0.1.0.0")]
