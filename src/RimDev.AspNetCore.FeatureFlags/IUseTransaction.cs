using System;
using System.Threading.Tasks;

namespace RimDev.AspNetCore.FeatureFlags
{
    public interface IUseTransaction : IDisposable
    {
        Task StartTransaction();
        Task CommitTransaction();
        Task AbortTransaction();
    }
}
