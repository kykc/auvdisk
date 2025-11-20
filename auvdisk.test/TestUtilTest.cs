namespace auvdisk.test;

public class TestUtilTest
{
    [Fact]
    public void TestFastHash()
    {
        using var stream1 = new MemoryStream();
        using var stream2 = new MemoryStream();
        using var stream3 = new MemoryStream();
        
        var writer1 = new StreamWriter(stream1);
        var writer2 = new StreamWriter(stream2);
        var writer3 = new StreamWriter(stream3);
        
        writer1.WriteLine("identical data");
        writer2.WriteLine("identical data");
        writer3.WriteLine("different data");
        
        writer1.Flush();
        writer2.Flush();
        writer3.Flush();
        
        Assert.Equal(TestUtil.CalculateAdler32([stream1]), TestUtil.CalculateAdler32([stream2]));
        Assert.NotEqual(TestUtil.CalculateAdler32([stream1]), TestUtil.CalculateAdler32([stream3]));
        Assert.Equal(TestUtil.CalculateAdler32([stream1, stream2]), TestUtil.CalculateAdler32([stream2, stream1]));
        Assert.Equal(TestUtil.CalculateAdler32([stream1, stream2, stream3]), TestUtil.CalculateAdler32([stream2, stream1, stream3]));
        Assert.NotEqual(TestUtil.CalculateAdler32([stream3, stream1, stream2]), TestUtil.CalculateAdler32([stream2, stream1, stream3]));
    }    
}