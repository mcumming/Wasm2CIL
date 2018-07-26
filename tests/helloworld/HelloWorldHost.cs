using System;

namespace HellowWorldHost
{
    class Program
    {
        public static void Main(string[] args)
        {

            var module = new HelloWorldProxy.HelloWorld(env);
            module.main();
        }
    }
}
