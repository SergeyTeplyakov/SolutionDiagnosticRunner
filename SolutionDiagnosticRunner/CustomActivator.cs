using System;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace SolutionDiagnosticRunner
{
    internal static class CustomActivator
    {
        public static T CreateInstance<T>(Type type)
        {
            try
            {
                return (T)Activator.CreateInstance(type);
            }
            catch (TargetInvocationException e)
            {
                var di = ExceptionDispatchInfo.Capture(e.InnerException);
                di.Throw();

                throw; // this code is unreachable!
            }
        }
    }
}