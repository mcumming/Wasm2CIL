using System;

namespace Factorial.Host
{
    class Program
    {
         public static void Main (string[] args)
        {
            var module = new FactorialProxy.Factorial();
            Console.WriteLine(module._Z4facti(Int32.Parse(args[0])));
        }
    }
}
