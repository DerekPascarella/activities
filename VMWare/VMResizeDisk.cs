using Ayehu.Sdk.ActivityCreation.Interfaces;
using Ayehu.Sdk.ActivityCreation.Extension;
using System.Text;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Data;
using System.Diagnostics;

namespace Ayehu.Sdk.ActivityCreation
{
    public class ActivityClass : IActivity
    {
        public string HostName;
        public string UserName;
        public string Password;
        public string VMName;
        public string DiskName;
        public string DiskSize;

        public ICustomActivityResult Execute()

        {
            StringWriter sw = new StringWriter();
            DataTable dt = new DataTable("resultSet");
            dt.Columns.Add("Result", typeof(String));
            string sResult = "";


            string command_path = "VMWare.exe";
            DataTable dtParams = new DataTable("Params");
            dtParams.Columns.Add("Command");
            dtParams.Columns.Add("UserName");
            dtParams.Columns.Add("Password");
            dtParams.Columns.Add("HostName");
            dtParams.Columns.Add("VMName");
            dtParams.Columns.Add("DiskName");
            dtParams.Columns.Add("DiskSize");

            DataRow rParams = dtParams.NewRow();
            rParams["Command"] = "VMResizeDisk";
            rParams["UserName"] = UserName;
            rParams["Password"] = Password;
            rParams["HostName"] = HostName;
            rParams["VMName"] = VMName;
            rParams["DiskName"] = DiskName;
            rParams["DiskSize"] = DiskSize;
            dtParams.Rows.Add(rParams);

            dtParams.WriteXml(sw, XmlWriteMode.WriteSchema, false);

            Process prVMWare = new Process();
            prVMWare.StartInfo.FileName = command_path;
            prVMWare.StartInfo.Arguments = "\"" + sw.ToString().Replace("\"", "\\\"") + "\"";
            prVMWare.StartInfo.UseShellExecute = false;
            prVMWare.StartInfo.CreateNoWindow = true;
            prVMWare.StartInfo.RedirectStandardError = true;
            prVMWare.StartInfo.RedirectStandardInput = true;
            prVMWare.StartInfo.RedirectStandardOutput = true;

            prVMWare.Start();
            StreamReader srResult = prVMWare.StandardOutput;
            sResult = srResult.ReadToEnd();

            srResult.Close();
            prVMWare.Close();



            if (sResult == "")
            {
                return this.GenerateActivityResult(dt);
            }
            else
            {
                return this.GenerateActivityResult(sResult);
            }
        }
    }
}
