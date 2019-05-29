using System.Threading.Tasks;
using org.apache.utils;
using Xunit;

namespace org.apache.zookeeper.recipes.@lock
{
	/// <summary>
	/// test for writelock
	/// </summary>
	public sealed class WriteLockTest : ClientBase
	{
        private static readonly ILogProducer LOG = TypeLogger<WriteLockTest>.Instance;

	    [Fact]
		public Task testRun()
		{
			return runTest(3);
		}

	    private sealed class LockCallback : LockListener
	    {
	        public TaskCompletionSource<bool> TaskCompletionSource = new TaskCompletionSource<bool>();
            
			public Task lockAcquired()
			{
                TaskCompletionSource.TrySetResult(true);
			    return Task.FromResult(0);
			}

			public Task lockReleased()
			{
                return Task.FromResult(0);
            }

		}

	    private async Task runTest(int count)
		{
			var nodes = new WriteLock[count];
            var lockCallback=new LockCallback();
			for (int i = 0; i < count; i++)
			{
				ZooKeeper keeper = await createClient();
				WriteLock leader = new WriteLock(keeper, "/test", null);
				leader.setLockListener(lockCallback);
				nodes[i] = leader;

				await leader.Lock();
			}

			// lets wait for any previous leaders to die and one of our new
			// nodes to become the new leader
            Assert.assertTrue(await lockCallback.TaskCompletionSource.Task.WithTimeout(30*1000));
            
			WriteLock first = nodes[0];
			dumpNodes(nodes,count);

			// lets assert that the first election is the leader
			Assert.assertTrue("The first znode should be the leader " + first.Id, first.Owner);

			for (int i = 1; i < count; i++)
			{
				WriteLock node = nodes[i];
				Assert.assertFalse("Node should not be the leader " + node.Id, node.Owner);
			}

			if (count > 1)
			{
			    LOG.debug("Now killing the leader");
                // now lets kill the leader
			    lockCallback.TaskCompletionSource = new TaskCompletionSource<bool>();
			    await first.unlock();
                Assert.assertTrue(await lockCallback.TaskCompletionSource.Task.WithTimeout(30 * 1000));
                WriteLock second = nodes[1];
			    dumpNodes(nodes, count);
			    // lets assert that the first election is the leader
			    Assert.assertTrue("The second znode should be the leader " + second.Id, second.Owner);

			    for (int i = 2; i < count; i++)
			    {
			        WriteLock node = nodes[i];
			        Assert.assertFalse("Node should not be the leader " + node.Id, node.Owner);
			    }
			}
		}

	    private static void dumpNodes(WriteLock[] nodes, int count)
		{
			for (int i = 0; i < count; i++)
			{
				WriteLock node = nodes[i];
                LOG.debug("node: " + i + " id: " + node.Id + " is leader: " + node.Owner);
			}
		}
	}

}