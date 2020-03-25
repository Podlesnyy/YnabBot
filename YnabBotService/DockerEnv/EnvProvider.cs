using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace Adp.YnabBotService.DockerEnv
{
    public sealed class EnvProvider : ConfigurationProvider
    {
        private readonly string envPath;

        public EnvProvider(string envPath)
        {
            this.envPath = envPath;
        }

        public override void Load()
        {
            if (!File.Exists(envPath))
                return;

            Data = File.ReadAllLines(envPath).Select(readAllLine => readAllLine.Split("=")).ToDictionary(item => item[0], item => item[1]);
        }
    }
}