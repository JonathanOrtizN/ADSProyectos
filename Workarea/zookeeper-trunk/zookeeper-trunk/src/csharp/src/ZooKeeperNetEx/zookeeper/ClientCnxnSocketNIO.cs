﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using org.apache.utils;
using ZooKeeperNetEx.utils;

namespace org.apache.zookeeper
{
    internal sealed class ClientCnxnSocketNIO : ClientCnxnSocket
	{
        private static readonly ILogProducer LOG = TypeLogger<ClientCnxnSocketNIO>.Instance;

	    private bool initialized;

		private Socket socket;
        
        internal readonly AwaitableSignal somethingIsPending = new AwaitableSignal();

	    private readonly VolatileBool readEnabled = new VolatileBool(false);

        private readonly VolatileBool writeEnabled = new VolatileBool(false);

	    private readonly VolatileReference<SocketContext> _socketAsyncEventArgsWrapper = new VolatileReference<SocketContext>(null);
        
	    internal ClientCnxnSocketNIO(ClientCnxn cnxn) : base(cnxn)
	    {
	    }

	    internal override bool isConnected() {
	        return socket != null;
	    }

        private void doIO()
		{
			var localSock = socket;
			if (localSock == null)
			{
				throw new IOException("Socket is null!");
			}
			if (Readable && _socketAsyncEventArgsWrapper.Value.GetResult() == SocketAsyncOperation.Receive)
			{
			    try
			    {
			        localSock.read(incomingBuffer);
                    _socketAsyncEventArgsWrapper.Value.StartReceiveAsync();
			    }
				catch(Exception e)
				{
					throw new EndOfStreamException("Unable to read additional data from server sessionid 0x" + sessionId.ToHexString() + ", likely server has closed socket",e);
				}
				if (!incomingBuffer.hasRemaining())
				{
					incomingBuffer.flip();
					if (incomingBuffer == lenBuffer)
					{
						recvCount++;
						readLength();
					}
					else if (!initialized)
					{
						readConnectResult();
						enableRead();
                        if (findSendablePacket() != null)
						{
							enableWrite();
						}
						lenBuffer.clear();
						incomingBuffer = lenBuffer;
						updateLastHeard();
						initialized = true;
					}
					else
					{
						clientCnxn.readResponse(incomingBuffer);
						lenBuffer.clear();
						incomingBuffer = lenBuffer;
						updateLastHeard();
					}
				}
			}
			if (Writable)
			{
                lock (clientCnxn.outgoingQueue)
				{
                    var pNode = findSendablePacket();
				    var p = pNode.Value;
					if (p != null)
					{
						updateLastSend();
						// If we already started writing p, p.bb will already exist
						if (p.bb == null)
						{
							if ((p.requestHeader != null) && (p.requestHeader.get_Type() != (int) ZooDefs.OpCode.ping) && (p.requestHeader.get_Type() != (int) ZooDefs.OpCode.auth)) {
                                p.requestHeader.setXid(clientCnxn.getXid());
							}
							p.createBB();
						}
						localSock.write(p.bb);
						if (!p.bb.hasRemaining())
						{
							sentCount++;
                            clientCnxn.outgoingQueue.Remove(pNode);
							if (p.requestHeader != null && p.requestHeader.get_Type() != (int) ZooDefs.OpCode.ping && p.requestHeader.get_Type() != (int) ZooDefs.OpCode.auth)
							{
                                lock (clientCnxn.pendingQueue)
								{
                                    clientCnxn.pendingQueue.AddLast(p);
								}
							}
						}
					}
                    if (clientCnxn.outgoingQueue.Count == 0)
					{
                        // No more packets to send: turn off write interest flag.
                        // Will be turned on later by a later call to enableWrite(),
                        // or in either doIO() or in doTransport() if not.
						disableWrite();
					}
					else if (!initialized && p != null && !p.bb.hasRemaining())
					{
                        p.bb.Stream.Dispose();
                        disableWrite();
					}
					else
					{
						// Just in case
						enableWrite();
					}
				}
			}
		}

	   
        
	    private LinkedListNode<ClientCnxn.Packet> findSendablePacket()
		{
            lock (clientCnxn.outgoingQueue)
			{
                if (clientCnxn.outgoingQueue.Count == 0)
                {
                    return null;
                }
                return clientCnxn.outgoingQueue.First;
			}
		}

		internal override async Task cleanup()
		{
            readEnabled.Value = false;
            writeEnabled.Value = false;
			if (socket != null)
			{
                await Task.Delay(100).ConfigureAwait(false);
                try
				{
                    socket.Shutdown(SocketShutdown.Receive);
				}
				catch (Exception e)
				{
					if (LOG.isDebugEnabled())
					{
						LOG.debug("Ignoring exception during shutdown input", e);
					}
				}
				try
				{
                    socket.Shutdown(SocketShutdown.Send);
				}
				catch (Exception e)
				{
					if (LOG.isDebugEnabled())
					{
						LOG.debug("Ignoring exception during shutdown output", e);
					}
				}
				try
				{
                    socket.Dispose();
				}
				catch (Exception e)
				{
					if (LOG.isDebugEnabled())
					{
                        LOG.debug("Ignoring exception during socket close", e);
					}
				}
			    try
				{
			         _socketAsyncEventArgsWrapper.Value.Dispose();
				}
				catch (Exception e)
				{
					if (LOG.isDebugEnabled())
					{
			            LOG.debug("Ignoring exception during SocketAsyncEventArgs dispose", e);
					}
				}
			}
			socket = null;
		}


        /// <summary>
        /// create a socket channel. </summary>
        /// <returns> the created socket channel </returns>
        private static Socket createSock(AddressFamily addressFamily)
	    {
			Socket sock=new Socket(addressFamily, SocketType.Stream, ProtocolType.Tcp);
			sock.Blocking = false;
			sock.LingerState = new LingerOption(false, 0);
			sock.NoDelay=true;
			return sock;
	    }

	    /// <summary>
		/// register with the selection and connect </summary>
        /// <param name="sock"> the <seealso cref="Socket"/> </param>
		/// <param name="addr"> the address of remote host </param>
        private void registerAndConnect(Socket sock, EndPoint addr)
		{
		    socket = sock;
            _socketAsyncEventArgsWrapper.Value = SocketContext.StartConnectAsync(somethingIsPending, sock, addr);
	    }

        internal override void connect(IPEndPoint addr)
		{
			Socket sock = createSock(addr.AddressFamily);
            
            try
			{
			   registerAndConnect(sock, addr);
			}
			catch (Exception e)
			{
			    LOG.error("Unable to open socket to " + addr, e);
                sock.Dispose();
				throw;
			}
			initialized = false;

			/*
			 * Reset incomingBuffer
			 */
			lenBuffer.clear();
			incomingBuffer = lenBuffer;
		}

        /// <summary>
        /// Returns the address to which the socket is connected.
        /// </summary>
        /// <returns> ip address of the remote side of the connection or null if not
        ///         connected </returns>
	    internal override EndPoint getRemoteSocketAddress() {
            // a lot could go wrong here, so rather than put in a bunch of code
            // to check for nulls all down the chain let's do it the simple
            // yet bulletproof way
            try
            {
                return _socketAsyncEventArgsWrapper.Value.RemoteEndPoint;
            }
            catch (NullReferenceException)
            {
                return null;
            }
	    }

		/// <summary>
		/// Returns the local address to which the socket is bound.
		/// </summary>
		/// <returns> ip address of the remote side of the connection or null if not
		///         connected </returns>
		internal override EndPoint getLocalSocketAddress()
		{
				// a lot could go wrong here, so rather than put in a bunch of code
				// to check for nulls all down the chain let's do it the simple
				// yet bulletproof way
				try
				{
                    return socket.LocalEndPoint;
				}
				catch (NullReferenceException)
				{
					return null;
				}
		}

		internal override void wakeupCnxn()
		{
            somethingIsPending.TrySignal();
		}

        internal override void doTransport() 
        {
            somethingIsPending.Reset();

            // Everything below and until we get back to the select is
			// non blocking, so time is effectively a constant. That is
			// Why we just have to do this once, here
			updateNow();

            if (_socketAsyncEventArgsWrapper.Value.GetResult() == SocketAsyncOperation.Connect)
            {
                updateLastSendAndHeard();
                clientCnxn.primeConnection();
                _socketAsyncEventArgsWrapper.Value.StartReceiveAsync();
            }

            doIO();

	        if (clientCnxn.getState().isConnected())
			{   
                lock (clientCnxn.outgoingQueue)
				{
                    if (findSendablePacket() != null)
				    {
						enableWrite();
				    }
				}
			}
		}

        private void enableWrite()
        {
            writeEnabled.Value = true;
            wakeupCnxn();
        }

        private void disableWrite()
        {
            writeEnabled.Value = false;
        }

	    private void enableRead()
        {
            readEnabled.Value = true;
        }
	    
        internal override void enableReadWriteOnly()
        {
            enableRead();
            enableWrite();
        }
	
	    private bool Writable
	    {
	        get
	        {
	            return writeEnabled.Value;
	        }
	    }

	    private bool Readable
	    {
	        get
	        {
	            return readEnabled.Value;
	        }
	    }
    }

}