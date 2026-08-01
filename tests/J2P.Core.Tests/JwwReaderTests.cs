using J2P.Core.Jww;
using J2P.Core.Jww.Entities;
using Xunit;

namespace J2P.Core.Tests;

public class JwwReaderTests
{
    [Fact]
    public void ReadHeader_基本情報を取得できる()
    {
        var builder = new JwwFixtureBuilder
        {
            PaperSizeCode = 2, // A2
            WriteGroup = 1,
            Memo = "テスト図面",
            PrintOriginX = -297.0,
            PrintOriginY = -210.0,
            PrintScale = 0.5,
            PrintFlags = 51, // 十位5=中中, 一位1=90°回転
        };
        builder.GroupScales[1] = 50.0;

        var header = JwwReader.ReadHeader(builder.BuildStream());

        Assert.Equal(420u, header.Version);
        Assert.Equal(2u, header.PaperSizeCode);
        Assert.Equal("テスト図面", header.Memo);
        Assert.Equal(1u, header.WriteGroup);
        Assert.Equal(50.0, header.ActiveScaleDenominator);
        Assert.Equal(-297.0, header.PrintOriginX);
        Assert.Equal(-210.0, header.PrintOriginY);
        Assert.Equal(0.5, header.PrintScale);
        Assert.True(header.PrintRotated);
        Assert.Equal(5u, header.PrintBasePosition);
    }

    [Fact]
    public void Read_全図形種を解析できる()
    {
        var builder = new JwwFixtureBuilder();
        builder
            .AddLine(0, 0, 100, 50, penColor: 2)
            .AddLine(-10, -20, 30, 40)          // 2本目はクラス参照タグ経由
            .AddArc(50, 50, 25, startAngle: 0.5, sweepAngle: 1.5, flatness: 0.8)
            .AddArc(0, 0, 10, fullCircle: true)
            .AddPoint(5, 5)
            .AddPoint(6, 6, temporary: true)
            .AddText(10, 10, 40, 10, "こんにちは図面", sizeX: 4, sizeY: 5)
            .AddSolid(0, 0, 10, 0, 0, 10, 10, 10, rgb: 0x00FF8040)
            .AddBlockInsert(100, 100, 2, 2, Math.PI / 2, defNumber: 7)
            .AddBlockDefinitionWithLine(7, "部品A", 0, 0, 5, 5);

        var doc = JwwReader.Read(builder.BuildStream());

        Assert.Equal(9, doc.Entities.Count);

        var line1 = Assert.IsType<JwwLine>(doc.Entities[0]);
        Assert.Equal(100.0, line1.X1);
        Assert.Equal(2, line1.PenColor);

        var line2 = Assert.IsType<JwwLine>(doc.Entities[1]);
        Assert.Equal(-20.0, line2.Y0);

        var arc = Assert.IsType<JwwArc>(doc.Entities[2]);
        Assert.Equal(25.0, arc.Radius);
        Assert.Equal(0.8, arc.Flatness);
        Assert.False(arc.IsFullCircle);

        var circle = Assert.IsType<JwwArc>(doc.Entities[3]);
        Assert.True(circle.IsFullCircle);

        var pt = Assert.IsType<JwwPoint>(doc.Entities[4]);
        Assert.False(pt.IsTemporary);
        var tempPt = Assert.IsType<JwwPoint>(doc.Entities[5]);
        Assert.True(tempPt.IsTemporary);

        var text = Assert.IsType<JwwText>(doc.Entities[6]);
        Assert.Equal("こんにちは図面", text.Text);
        Assert.Equal("ＭＳ ゴシック", text.FontName);
        Assert.Equal(5.0, text.SizeY);

        var solid = Assert.IsType<JwwSolid>(doc.Entities[7]);
        Assert.Equal(0x00FF8040u, solid.Rgb);

        var insert = Assert.IsType<JwwBlockInsert>(doc.Entities[8]);
        Assert.Equal(7u, insert.DefinitionNumber);
        Assert.Equal(Math.PI / 2, insert.Rotation);

        var def = Assert.Single(doc.BlockDefinitions).Value;
        Assert.Equal(7, def.Number);
        Assert.Equal("部品A", def.Name);
        var childLine = Assert.IsType<JwwLine>(Assert.Single(def.Entities));
        Assert.Equal(5.0, childLine.X1);
    }

    [Fact]
    public void Read_バージョン700では画像セクションまで読める()
    {
        var builder = new JwwFixtureBuilder { Version = 700 };
        builder.AddLine(0, 0, 1, 1);
        builder.AddBlockDefinitionWithLine(1, "B", 0, 0, 1, 1);

        var doc = JwwReader.Read(builder.BuildStream());

        Assert.Equal(700u, doc.Header.Version);
        Assert.Single(doc.Entities);
        Assert.Single(doc.BlockDefinitions);
        Assert.Empty(doc.EmbeddedImageNames);
    }

    [Fact]
    public void Read_シグネチャ不一致はJwwFormatException()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        Assert.Throws<JwwFormatException>(() => JwwReader.Read(new MemoryStream(bytes)));
    }

    [Fact]
    public void Read_途中で切れたファイルはJwwFormatException()
    {
        var builder = new JwwFixtureBuilder();
        builder.AddLine(0, 0, 100, 100);
        var bytes = builder.Build();
        var truncated = bytes.AsSpan(0, bytes.Length / 2).ToArray();

        Assert.Throws<JwwFormatException>(() => JwwReader.Read(new MemoryStream(truncated)));
    }

    [Fact]
    public void Read_旧バージョン300はレイアウト差分を吸収して読める()
    {
        var builder = new JwwFixtureBuilder { Version = 300, PaperSizeCode = 4 };
        builder.AddLine(1, 2, 3, 4);

        var doc = JwwReader.Read(builder.BuildStream());

        Assert.Equal(4u, doc.Header.PaperSizeCode);
        var line = Assert.IsType<JwwLine>(Assert.Single(doc.Entities));
        Assert.Equal(3.0, line.X1);
    }

    [Fact]
    public void ReadHeader_255バイト超の文字列は長さエスケープ経由で読める()
    {
        var longMemo = new string('あ', 300); // CP932で600バイト → 0xFF+WORD 長さ
        var builder = new JwwFixtureBuilder { Memo = longMemo };

        var header = JwwReader.ReadHeader(builder.BuildStream());

        Assert.Equal(longMemo, header.Memo);
    }

    [Fact]
    public void PaperSizes_主要コードの寸法()
    {
        Assert.Equal((420, 297), JwwPaperSizes.GetSizeMm(3));
        Assert.Equal((297, 210), JwwPaperSizes.GetSizeMm(4));
        Assert.Equal("A3", JwwPaperSizes.GetName(3));
        Assert.Equal((1189, 841), JwwPaperSizes.GetSizeMm(0));
    }
}
