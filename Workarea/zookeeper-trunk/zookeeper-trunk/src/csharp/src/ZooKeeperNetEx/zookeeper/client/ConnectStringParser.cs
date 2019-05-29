﻿using System;
using System.Collections.Generic;
using System.Linq;
using org.apache.zookeeper.common;

namespace org.apache.zookeeper.client
{
    /// <summary>
	/// A parser for ZooKeeper Client connect strings.
	/// 
	/// This class is not meant to be seen or used outside of ZooKeeper itself.
	/// 
	/// The chrootPath member should be replaced by a Path object in issue
	/// ZOOKEEPER-849.
	/// </summary>
    internal sealed class ConnectStringParser
	{
		private const int DEFAULT_PORT = 2181;

		private readonly string chrootPath;

        private readonly List<HostAndPort> serverAddresses = new List<HostAndPort>();

        private static readonly char[] splitter = {','};

		public ConnectStringParser(string connectString)
		{
			// parse out chroot, if any
			int off = connectString.IndexOf('/');
			if (off >= 0)
			{
				string chPath = connectString.Substring(off);
				// ignore "/" chroot spec, same as null
				if (chPath.Length == 1)
				{
					chrootPath = null;
				}
				else
				{
					PathUtils.validatePath(chPath);
					chrootPath = chPath;
				}
				connectString = connectString.Substring(0, off);
			}
			else
			{
				chrootPath = null;
			}

		    string[] hostsList = connectString.Split(splitter, StringSplitOptions.RemoveEmptyEntries);
			foreach (string host in hostsList)
			{
				int port = DEFAULT_PORT;
				int pidx = host.LastIndexOf(':');
			    string parsedHost = host;
				if (pidx >= 0)
				{
					// otherwise : is at the end of the string, ignore
					if (pidx < host.Length - 1) 
                    {
					    port = int.Parse(host.Substring(pidx + 1));
					}
                    parsedHost = host.Substring(0, pidx);
				}
			    serverAddresses.Add(new HostAndPort(parsedHost, port));
			}
		}

        public string getChrootPath()
        {
            return chrootPath;
        }

        public List<HostAndPort> getServerAddresses()
        {
                return serverAddresses.Distinct().ToList();
        }
	}
}