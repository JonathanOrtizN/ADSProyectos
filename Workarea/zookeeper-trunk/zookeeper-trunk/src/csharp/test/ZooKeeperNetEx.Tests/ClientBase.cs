using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace org.apache.zookeeper 
{
    [Collection("Setup")]
    public abstract class ClientBase:IAsyncLifetime {

        protected static class Arrays {
            public static List<T> asList<T>(params T[]objs) {
                return new List<T>(objs);
            }
        }

        public const int CONNECTION_TIMEOUT = 4000;
        private string m_currentRoot;
        private ZooKeeper m_rootZK;

        private const string hostPort = "127.0.0.1,localhost";

        private readonly ConcurrentBag<ZooKeeper> allClients = new ConcurrentBag<ZooKeeper>();
        
        protected Task<ZooKeeper> createClient(string chroot = null)
        {
            return createClient(NullWatcher.Instance, chroot);
        }

        protected async Task<ZooKeeper> createClient(Watcher watcher, string chroot=null)
        {
            if (watcher == null) watcher = NullWatcher.Instance;
            var zk = new ZooKeeper(hostPort + m_currentRoot + chroot, CONNECTION_TIMEOUT, watcher);
            allClients.Add(zk);
            if (!await zk.connectedSignal.Task.WithTimeout(CONNECTION_TIMEOUT)) {
                Assert.fail("Unable to connect to server");
            }

            return zk;
        }

        public virtual async Task InitializeAsync()
        {
            m_rootZK = await createClient();
            m_currentRoot = await m_rootZK.createAsync("/", null, ZooDefs.Ids.OPEN_ACL_UNSAFE, CreateMode.PERSISTENT_SEQUENTIAL);
        }
        
        public virtual async Task DisposeAsync()
        {
            await ZKUtil.deleteRecursiveAsync(m_rootZK, m_currentRoot);
            await Task.WhenAll(allClients.Select(c => c.closeAsync()));
        }

        /// <summary>
        ///     In general don't use this. Only use in the special case that you
        ///     want to ignore results (for whatever reason) in your test. Don't
        ///     use empty watchers in real code!
        /// </summary>
        public class NullWatcher : Watcher
        {
            public static readonly NullWatcher Instance = new NullWatcher();
            private NullWatcher() { }
            public override Task process(WatchedEvent @event)
            {
                return CompletedTask;
                // nada
            }
        }
    }
}