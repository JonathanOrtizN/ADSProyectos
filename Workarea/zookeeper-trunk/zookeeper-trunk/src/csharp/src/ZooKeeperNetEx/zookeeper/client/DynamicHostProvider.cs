﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using org.apache.utils;

// ReSharper disable PossibleMultipleEnumeration
namespace org.apache.zookeeper.client
{
    /// <summary>
    /// This HostProvider resolves its hosts on first call to next(). Then, after 
    /// returning all resolved IPs, it resolves again.
    /// </summary>
    internal sealed class DynamicHostProvider : HostProvider
	{
        private readonly List<HostAndPort> m_UnresolvedEndPoints;

        private List<ResolvedEndPoint> m_ResolvedEndPoints;

        internal ResolvedEndPoint LastIP { get; private set; }

        internal int CurrentIndex { get; private set; } = -1;

        internal bool ResolvingInBackground => m_ResolvingTask != null && !m_ResolvingTask.IsCompleted;

        internal bool FirstDnsTry { get; private set; } = true;

        private Task<List<ResolvedEndPoint>> m_ResolvingTask;

	    private readonly ILogProducer m_Log;

	    private readonly IDnsResolver m_DnsResolver;

        public DynamicHostProvider(List<HostAndPort> serverAddresses, IDnsResolver dnsResolver = null,
            ILogProducer log = null)
        {
            if (serverAddresses.Count == 0)
            {
                throw new ArgumentException("A HostProvider may not be empty!");
            }
            m_Log = log ?? new TypeLogger<DynamicHostProvider>();
            m_DnsResolver = dnsResolver ?? new DnsResolver(m_Log);
            m_UnresolvedEndPoints = serverAddresses;
        }

        public int size()
		{
			return m_ResolvedEndPoints.Count;
		}

	    public async Task<ResolvedEndPoint> next(int spinDelay)
	    {
	        ResolvedEndPoint nextEndPoint;
	        if (m_ResolvedEndPoints == null)
	        {
	            m_ResolvedEndPoints = await ResolveAtLeastOneAndShuffle(m_UnresolvedEndPoints, spinDelay).ConfigureAwait(false);
	            nextEndPoint = m_ResolvedEndPoints[0];
	        }
	        else
	        {
	            ++CurrentIndex;
	            if (CurrentIndex == m_ResolvedEndPoints.Count)
	            {
	                CurrentIndex = 0;
	            }
	            if (m_ResolvingTask != null && m_ResolvingTask.IsCompleted)
	            {
	                var resolved = await m_ResolvingTask.ConfigureAwait(false);
	                m_ResolvingTask = null;
	                if (resolved.Count > 0)
	                {
                        if (resolved.Contains(LastIP))
	                    {
	                        resolved.Remove(LastIP);
	                        resolved.Add(LastIP);
	                    }
	                    else
	                    {
	                        LastIP = null;
	                    }
                        CurrentIndex = 0;
                        m_ResolvedEndPoints = resolved;
	                }
	                else
	                {
	                    m_Log.debug("Keeping the current resolved IPs since background resolution failed");
	                }
	            }
	            nextEndPoint = m_ResolvedEndPoints[CurrentIndex];
	            if (nextEndPoint == LastIP && spinDelay > 0)
	            {
	                if (m_ResolvingTask == null)
	                {
	                    m_ResolvingTask = ResolveAndShuffle(m_UnresolvedEndPoints);
	                }
                    await Task.Delay(spinDelay).ConfigureAwait(false);
	            }
	            else if (LastIP == null)
	            {
	                LastIP = m_ResolvedEndPoints[0];
	            }
	        }
	        return nextEndPoint;
	    }

	    public void onConnected()
		{
		    LastIP = m_ResolvedEndPoints[CurrentIndex];
		}

	    private async Task<List<ResolvedEndPoint>> ResolveAtLeastOneAndShuffle(IEnumerable<HostAndPort> unresolvedEndPoints, int spinDelay)
        {
            var unresolvedForLog = unresolvedEndPoints.ToCommaDelimited();
	        if (FirstDnsTry)
	        {
	            FirstDnsTry = false;
	        }
	        else
	        {
	            m_Log.debug("Since we couldn't resolve any IPs yet, we sleep for a second before retying");
	            await Task.Delay(spinDelay).ConfigureAwait(false);
	        }
	        m_Log.debug($"Trying to resolve at least one IP from hosts:{{{unresolvedForLog}}}");
            var resolved = await ResolveAndShuffle(unresolvedEndPoints).ConfigureAwait(false);
            if (!resolved.Any())
            {
                m_Log.debug($"Failed to resolve any IP from hosts:{{{unresolvedForLog}}}");
                throw new SocketException((int) SocketError.HostUnreachable);
            }
            return resolved;
        }

	    private async Task<List<ResolvedEndPoint>> ResolveAndShuffle(IEnumerable<HostAndPort> unresolvedEndPoints)
        {
            var resolvedEndPoints = await m_DnsResolver.Resolve(unresolvedEndPoints).ConfigureAwait(false);
            return resolvedEndPoints.OrderBy(i => Guid.NewGuid()).ToList();
        }
    }
}