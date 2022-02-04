
namespace UACommander
{
    using System.Threading;
    using System.Threading.Tasks;

    public interface IMethodHandler
    {
        Task RegisterMethodsAsync(string connectionString, CancellationToken cancellationToken = default);
    }
}
