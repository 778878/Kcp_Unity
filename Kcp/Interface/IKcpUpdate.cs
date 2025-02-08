using System.Threading.Tasks;

namespace Kcp.Interface
{
    public interface IKcpUpdate
    {
        Task Update(ulong currentTime);
    }
}

