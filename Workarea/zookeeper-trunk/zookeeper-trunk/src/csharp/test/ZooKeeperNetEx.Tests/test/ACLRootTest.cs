using System.Threading.Tasks;
using Xunit;

namespace org.apache.zookeeper.test
{
    public sealed class ACLRootTest : ClientBase
	{
        [Fact]
		public async Task testRootAcl()
		{
			var zk = await createClient();
				// set auth using digest
				zk.addAuthInfo("digest", "pat:test".UTF8getBytes());
				await zk.setACLAsync("/", ZooDefs.Ids.CREATOR_ALL_ACL, -1);
				await zk.getDataAsync("/", false);
                await zk.closeAsync();
				// verify no access
				zk = await createClient();
				try
				{
					await zk.getDataAsync("/", false);
					Assert.fail("validate auth");
				}
				catch (KeeperException.NoAuthException)
				{
					// expected
				}
				try
				{
					await zk.createAsync("/apps", null, ZooDefs.Ids.CREATOR_ALL_ACL, CreateMode.PERSISTENT);
					Assert.fail("validate auth");
				}
				catch (KeeperException.InvalidACLException)
				{
					// expected
				}
				zk.addAuthInfo("digest", "world:anyone".UTF8getBytes());
				try
				{
					await zk.createAsync("/apps", null, ZooDefs.Ids.CREATOR_ALL_ACL, CreateMode.PERSISTENT);
					Assert.fail("validate auth");
				}
				catch (KeeperException.NoAuthException)
				{
					// expected
				}
                await zk.closeAsync();
				// verify access using original auth
				zk = await createClient();
				zk.addAuthInfo("digest", "pat:test".UTF8getBytes());
				await zk.getDataAsync("/", false);
				await zk.createAsync("/apps", null, ZooDefs.Ids.CREATOR_ALL_ACL, CreateMode.PERSISTENT);
				await zk.deleteAsync("/apps", -1);
				// reset acl (back to open) and verify accessible again
				await zk.setACLAsync("/", ZooDefs.Ids.OPEN_ACL_UNSAFE, -1);
                await zk.closeAsync();

                zk = await createClient();
				await zk.getDataAsync("/", false);
				await zk.createAsync("/apps", null, ZooDefs.Ids.OPEN_ACL_UNSAFE, CreateMode.PERSISTENT);
				try
				{
					await zk.createAsync("/apps", null, ZooDefs.Ids.CREATOR_ALL_ACL, CreateMode.PERSISTENT);
					Assert.fail("validate auth");
				}
				catch (KeeperException.InvalidACLException)
				{
					// expected
				}
				await zk.deleteAsync("/apps", -1);
				zk.addAuthInfo("digest", "world:anyone".UTF8getBytes());
				await zk.createAsync("/apps", null, ZooDefs.Ids.CREATOR_ALL_ACL, CreateMode.PERSISTENT);
                await zk.closeAsync();

                zk = await createClient();
				await zk.deleteAsync("/apps", -1);
		}
	}

}