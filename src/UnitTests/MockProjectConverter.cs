using System;
using System.Threading;
using System.Threading.Tasks;

namespace PackagesConfigProjectConverter.UnitTests
{
    public class MockProjectConverter : IProjectConverter
    {
        public MockProjectConverter(ProjectConverterSettings settings)
        {
            Settings = settings;
        }

        public CancellationToken CancellationToken { get; set; }
        public TimeSpan Delay { get; set; } = TimeSpan.Zero;
        public ProjectConverterSettings Settings { get; }

        public void ConvertRepository(CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;

            Task.Delay(Delay, cancellationToken).Wait(cancellationToken);
        }

        public void Dispose()
        {
        }
    }
}