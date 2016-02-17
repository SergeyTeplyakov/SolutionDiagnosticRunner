using System.Linq;
using Microsoft.CodeAnalysis;

namespace SolutionDiagnosticRunner
{
    internal static class SolutionExtensions
    {
        public static int DocumentsCount(this Solution solution)
        {
            return solution.Projects.Sum(p => p.DocumentIds.Count);
        }
    }
}