namespace StickyTodo.Core;

public static class ProjectCatalog {
    private static readonly HashSet<string> excludedNames = new(StringComparer.OrdinalIgnoreCase) {
        "docs", "artifacts", "node_modules", "bin", "obj"
    };

    public static List<string> discover(string root) {
        return new DirectoryInfo(root).EnumerateDirectories()
            .Where(directory => !directory.Name.StartsWith('.')
                && (directory.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0
                && !excludedNames.Contains(directory.Name) && directory.Name != "其他")
            .Select(directory => directory.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Append("其他").ToList();
    }

    public static List<string> merge(IEnumerable<string> projects, IEnumerable<string> historicalProjects) {
        return historicalProjects.Concat(projects)
            .Where(name => !name.Equals("其他", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Append("其他").ToList();
    }
}
