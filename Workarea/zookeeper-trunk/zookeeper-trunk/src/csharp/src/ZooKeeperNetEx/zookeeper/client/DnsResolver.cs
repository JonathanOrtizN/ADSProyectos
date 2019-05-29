﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using org.apache.utils;
// ReSharper disable PossibleMultipleEnumeration

namespace org.apache.zookeeper.client
{
    internal class DnsResolver : IDnsResolver
    {
        private const int DNS_TIMEOUT = 10000;
        private readonly ILogProducer log;

        public DnsResolver(ILogProducer log)
        {
            this.log = log;
        }

        private static void IgnoreTask(Task task)
        {
            if (task.IsCompleted)
            {
                var ignored = task.Exception;
            }
            else
            {
                task.ContinueWith(
                    t => { var ignored = t.Exception; },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        private async Task<IEnumerable<ResolvedEndPoint>> Resolve(HostAndPort hostAndPort)
        {
            string host = hostAndPort.Host;
            log.debug($"Resolving Host={host}");
            var dnsTimeoutTask = Task.Delay(DNS_TIMEOUT);
            var dnsResolvingTask = Dns.GetHostAddressesAsync(host);
            await Task.WhenAny(dnsResolvingTask, dnsTimeoutTask).ConfigureAwait(false);
            if (dnsTimeoutTask.IsCompleted)
            {
                
                IgnoreTask(dnsResolvingTask);
                log.warn($"Timeout of {DNS_TIMEOUT}ms elapsed when resolving Host={host}");
            }
            else
            {
                try
                {
                    var allResolvedIPs = await dnsResolvingTask.ConfigureAwait(false);
                    log.debug($"Resolved Host={host} to {{{allResolvedIPs.ToCommaDelimited()}}}");
                    return allResolvedIPs.Select(ip => new ResolvedEndPoint(ip, hostAndPort));
                }
                catch (Exception e)
                {
                    log.error($"Failed resolving Host={host}", e);
                }
            }
            return Enumerable.Empty<ResolvedEndPoint>();
        }

        public async Task<IEnumerable<ResolvedEndPoint>> Resolve(IEnumerable<HostAndPort> unresolvedHosts)
        {
            var unresolvedForLog = unresolvedHosts.ToCommaDelimited();
            log.debug($"Resolving Hosts={{{unresolvedForLog}}}");
            var resolved = new List<ResolvedEndPoint>();
            var resolvingTasks = new List<Task<IEnumerable<ResolvedEndPoint>>>();
            foreach (var hostAndPort in unresolvedHosts)
            {
                IPAddress ip;
                if (IPAddress.TryParse(hostAndPort.Host, out ip))
                {
                    resolved.Add(new ResolvedEndPoint(ip, hostAndPort.Port));
                }
                else
                {
                    resolvingTasks.Add(Resolve(hostAndPort));
                }
            }
            var resolvedTasks = (await Task.WhenAll(resolvingTasks).ConfigureAwait(false)).SelectMany(i => i);
            resolved.AddRange(resolvedTasks);
            var res = resolved.Distinct();
            log.debug($"Resolved Hosts={{{unresolvedForLog}}} to {{{res.ToCommaDelimited()}}}");
            return res;
        }
    }
}
