namespace J2P.Core.Jww;

/// <summary>JWWファイルの解析に失敗したときに送出される例外。</summary>
public sealed class JwwFormatException : Exception
{
    public JwwFormatException(string message) : base(message) { }
    public JwwFormatException(string message, Exception inner) : base(message, inner) { }
}
