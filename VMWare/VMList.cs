using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
// ReSharper disable InconsistentNaming

namespace dValuate
{

    public class EvalRunTime
    {
        private const string POWERCLI_NAME = "VMware.VimAutomation.Core";

        public string Execute(string HostName, string UserName, string Password, string FilterApplied, string Cluster, string Datastore, string Folder)
        {
            if (string.IsNullOrEmpty(FilterApplied) == false && bool.Parse(FilterApplied) && string.IsNullOrEmpty(Cluster) && string.IsNullOrEmpty(Datastore) && string.IsNullOrEmpty(Folder))
            {
                throw new Exception("Filter settings are empty.");
            }

            if (string.IsNullOrEmpty(FilterApplied))
            {
                // Clear everything if FilterApplied is not send
                Cluster = Datastore = Folder = string.Empty;
            }

            if (string.IsNullOrEmpty(FilterApplied) == false && bool.Parse(FilterApplied) == false)
            {
                // Clear everything if FilterApplied = false
                Cluster = Datastore = Folder = string.Empty;
            }

            var dataTable = new DataTable("resultSet");

            using (var instance = new PowerShellProcessInstance(new Version(4, 0), null, null, false))
            {
                using (var runspace = RunspaceFactory.CreateOutOfProcessRunspace(new TypeTable(new string[0]), instance))
                {
                    runspace.Open();

                    using (var powerShellInstance = PowerShell.Create(RunspaceMode.NewRunspace))
                    {
                        powerShellInstance.Runspace = runspace;

                        // ---------------
                        ExecuteScript(powerShellInstance, "Set-ExecutionPolicy -ExecutionPolicy Unrestricted -Scope Process -Force ");

                        // ---------------
                        ExecuteScript(powerShellInstance, @"$PSVersionTable.PSVersion");
                        // System.Diagnostics.Trace.WriteLine($"=== Locally installed Powershell version: {powershellVersion.ToString()}");

                        // Snapins cmdlets could be not installed
                        // https://github.com/PowerShell/PowerShell/issues/6135
                        var pssapinInstalled = ExecuteScript(powerShellInstance, @"Get-Command | where { $_.Name -eq 'Get-PSSnapin'}");
                        if (pssapinInstalled.Any())
                        {
                            // Check if SnapIn already loaded
                            var loadedSnapins = ExecuteScript(powerShellInstance, "Get-PSSnapin");
                            if (loadedSnapins.Any(item => item.ToString().StartsWith(POWERCLI_NAME, StringComparison.OrdinalIgnoreCase)))
                            {
                                // Already loaded
                            }
                            else
                            {
                                // Check if could be loaded
                                var registedSnapins = ExecuteScript(powerShellInstance, "Get-PSSnapin -Registered");
                                if (registedSnapins.Any(item => item.ToString().StartsWith(POWERCLI_NAME, StringComparison.OrdinalIgnoreCase)))
                                {
                                    // Load SnapIn
                                    ExecuteScript(powerShellInstance, "Add-PSSnapin -Name '" + POWERCLI_NAME + "'");
                                }
                                else
                                {
                                    // VMware.VimAutomation.Core Snapin is not installed - so may be it in modules ?
                                    LoadWithModules(powerShellInstance);
                                }
                            }
                        }
                        else
                        {
                            LoadWithModules(powerShellInstance);
                        }

                        // Normalization command that will handle incorrect certificates
                        ExecuteScript(powerShellInstance, @"Set-PowerCLIConfiguration -DefaultVIServerMode Single -InvalidCertificateAction Ignore -Scope Session  -Confirm:$false");

                        // Fix case where Username send domain\username to just username
                        if (UserName.Contains("\\"))
                        {
                            UserName = UserName.Substring(UserName.LastIndexOf("\\", StringComparison.Ordinal) + 1);
                        }

                        // Connect
                        ExecuteScript(powerShellInstance, "Connect-VIServer -Server '" + HostName + "' -User '" + UserName + "' -Password '" + Password + "' -ErrorAction Continue", "Username is: " + UserName + " for host: " + HostName);

                        // Actual command
                        var command = string.IsNullOrEmpty(Cluster) ? string.IsNullOrEmpty(Datastore) ? string.IsNullOrEmpty(Folder) ? "Get-VM;" : "Get-VM -Location '" + Folder + "' -ErrorAction Stop ;" : "Get-Datastore -Name '" + Datastore + "' -ErrorAction Stop | Get-VM;" : "Get-Cluster '" + Cluster + "' -ErrorAction Stop | Get-VM ;";

                        if (string.IsNullOrEmpty(command) == false)
                        {
                            var commandResult = ExecuteScript(powerShellInstance, command);

                            commandResult.ToList().ForEach(item =>
                            {
                                var row = dataTable.NewRow();

                                item.Properties.ToList().ForEach(details =>
                                {
                                    if (dataTable.Columns.Contains(details.Name) == false)
                                    {
                                        dataTable.Columns.Add(details.Name);
                                    }

                                    row[details.Name] = details.Value;
                                });

                                if (row.ItemArray.Any())
                                {
                                    dataTable.Rows.Add(row);
                                }
                            });
                        }
                    }

                    runspace.Close();
                    runspace.Dispose();
                }
            }

            // ------------------------------------------------------------------------
            if (dataTable.Columns.Count == 0)
            {
                dataTable.Columns.Add("Result", typeof(string));
            }

            var view = new DataView(dataTable);
            var selected = view.ToTable("resultSet", false, "Name", "VMHost", "Folder");
            var stringWriter = new StringWriter();

            selected.WriteXml(stringWriter, XmlWriteMode.WriteSchema, false);
            selected.Dispose();

            return stringWriter.ToString();
        }

        private IEnumerable<PSObject> ExecuteScript(PowerShell session, string script, string additionalData = "")
        {
            session.AddScript(script);
            var result = session.Invoke();

            if (session.HadErrors && session.Streams.Error.Count > 0)
            {
                var errorMessage = session.Streams.Error
                    .Select(error => error.Exception.Message)
                    .Aggregate(string.Empty, (accum, item) => accum + item + "\n");

                errorMessage += "\n" + additionalData;

                session.Commands.Clear();
                session.Streams.ClearStreams();

                throw new ApplicationException("Error occurred while trying to run PowerShell script !\nAdditional information:\n" + errorMessage);
            }

            session.Commands.Clear();
            session.Streams.ClearStreams();

            return result;
        }

        private void LoadWithModules(PowerShell powerShellInstance)
        {
            var loadedModules = ExecuteScript(powerShellInstance, "Get-Module");
            if (loadedModules.Any(item => item.ToString().StartsWith(POWERCLI_NAME, StringComparison.OrdinalIgnoreCase)))
            {
                // Module already loaded for this session
            }
            else
            {
                var availableModules = ExecuteScript(powerShellInstance, "Get-Module -ListAvailable");
                if (availableModules.Any(item => item.ToString().StartsWith(POWERCLI_NAME, StringComparison.OrdinalIgnoreCase)))
                {
                    // Module exist - we should load it
                    ExecuteScript(powerShellInstance, "Import-Module -Name '" + POWERCLI_NAME + "'");
                }
                else
                {
                    // Module does not exist at all
                    throw new ApplicationException("Module '" + POWERCLI_NAME + "' does not exist!");
                }
            }
        }
    }

}
