﻿namespace Intellisense.FileSystem;

/// <summary>
/// Guarantees that the path either starts with a directory separator (i.e. /) or a drive letter followed by a volume separator (i.e. C:).
/// </summary>
internal readonly record struct RootedPath
{
    public string Value { get; }
    private RootedPath(string path)
    {
        Value = path;
    }

    public static RootedPath? Create(string path)
    {
        if (!Path.IsPathRooted(path))
        {
            return null;
        }

        return new RootedPath(path);
    }

    public static RootedPath CreateOrThrow(string path)
    {
        if (Create(path) is { } res)
        {
            return res;
        }

        throw new ArgumentException($"Attempted to create {nameof(RootedPath)} from {path} but it was not rooted or it was an invalid path.");
    }

    public bool IsRootDirectory()
    {

        if (Value[0].IsDirectorySeparator())
        {
            return Value.Length is 1;
        }

        if (Value.Length is 2)
        {
            return false;
        }

        return Value.Length is 3 && Value[2].IsDirectorySeparator();
    }

}
