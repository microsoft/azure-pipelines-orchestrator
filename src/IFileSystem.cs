public interface IFileSystem {
    string ReadAllText(string path);
}
public class FileSystem : IFileSystem
{
    public virtual string ReadAllText(string path) => File.ReadAllText(path);
}