using System.Threading.Tasks;
using org.apache.utils;
using Xunit;

namespace org.apache.zookeeper.test
{
    public sealed class ChrootTest : ClientBase
	{
		private sealed class MyWatcher : Watcher
		{
            private static readonly ILogProducer LOG = TypeLogger<Watcher>.Instance;
            private readonly string path;
		    private string eventPath;
		    private readonly TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>();

			public MyWatcher(string path)
			{
				this.path = path;
			}
			public override Task process(WatchedEvent @event)
			{
                LOG.debug("latch:" + path + " " + @event.getPath());
				eventPath = @event.getPath();
                taskCompletionSource.TrySetResult(true);
			    return CompletedTask;
			}


		    public async Task<bool> matches()
		    {
		        if (await taskCompletionSource.Task.WithTimeout(CONNECTION_TIMEOUT) == false)
		        {
		            Assert.fail("No watch received within timeout period " + path);
		        }
		        return path.Equals(eventPath);
		    }
		}



        [Fact]
		public async Task testChrootSynchronous()
		{
			ZooKeeper zk1 = await createClient();

				await zk1.createAsync("/ch1", null, ZooDefs.Ids.OPEN_ACL_UNSAFE, CreateMode.PERSISTENT);

            await zk1.closeAsync();

            ZooKeeper zk2 = await createClient("/ch1");

				Assert.assertEquals("/ch2", await zk2.createAsync("/ch2", null, ZooDefs.Ids.OPEN_ACL_UNSAFE, CreateMode.PERSISTENT));

            await zk2.closeAsync();

            zk1 = await createClient();
			zk2 = await createClient("/ch1");

				// check get
				MyWatcher w1 = new MyWatcher("/ch1");
				Assert.assertNotNull(await zk1.existsAsync("/ch1", w1));
				MyWatcher w2 = new MyWatcher("/ch1/ch2");
				Assert.assertNotNull(await zk1.existsAsync("/ch1/ch2", w2));

				MyWatcher w3 = new MyWatcher("/ch2");
				Assert.assertNotNull(await zk2.existsAsync("/ch2", w3));

				// set watches on child
				MyWatcher w4 = new MyWatcher("/ch1");
				await zk1.getChildrenAsync("/ch1",w4);
				MyWatcher w5 = new MyWatcher("/");
				await zk2.getChildrenAsync("/",w5);

				// check set
				await zk1.setDataAsync("/ch1", "1".UTF8getBytes(), -1);
				await zk2.setDataAsync("/ch2", "2".UTF8getBytes(), -1);

				// check watches
				Assert.assertTrue(await w1.matches());
				Assert.assertTrue(await w2.matches());
				Assert.assertTrue(await w3.matches());

				// check exceptions
				try
				{
					await zk2.setDataAsync("/ch3", "3".UTF8getBytes(), -1);
				}
				catch (KeeperException.NoNodeException e)
				{
					Assert.assertEquals("/ch3", e.getPath());
				}

                Assert.assertEquals("1".UTF8getBytes(), (await zk1.getDataAsync("/ch1", false)).Data);
				Assert.assertEquals("2".UTF8getBytes(), (await zk1.getDataAsync("/ch1/ch2", false)).Data);
				Assert.assertEquals("2".UTF8getBytes(), (await zk2.getDataAsync("/ch2", false)).Data);

                // check delete
                await zk2.deleteAsync("/ch2", -1);
				Assert.assertTrue(await w4.matches());
				Assert.assertTrue(await w5.matches());

				await zk1.deleteAsync("/ch1", -1);
				Assert.assertNull(await zk1.existsAsync("/ch1", false));
				Assert.assertNull(await zk1.existsAsync("/ch1/ch2", false));
				Assert.assertNull(await zk2.existsAsync("/ch2", false));

		}
	}

}