# NOTICE — サードパーティ由来物と出自の記録

本プロジェクト（J2P）は MIT ライセンスで提供されます。以下に、実装の参照元と同梱物の出自を記録します。

## JWW ファイルフォーマットの実装出自

- **JWW パーサ（`src/J2P.Core/Jww/`）は純 C# の独自実装**です。
- 移植・参照元は次の 2 つに限定しています:
  - [JinkiKeikaku/JwwExchange](https://github.com/JinkiKeikaku/JwwExchange) — JWW/JWS 読み書きの C++ ライブラリ。
    **The Unlicense（パブリックドメイン）** で公開されており、本プロジェクトへの移植に制約はありません。
  - Jw_cad 付属のフォーマット説明文書（jwdatafmt.txt, Ver.5.00a 時点）— データ構造の仕様記述として参照。
- **LibreCAD の jwwlib（GPLv2）のソースコードは一切コピー・移植していません。**
  挙動の突き合わせ（同じ入力ファイルに対する解釈の確認）のみに使用しています。

## 同梱フォント

- `src/J2P.Core/Pdf/Fonts/` に以下のフォントを同梱し、PDF へ埋め込みます:
  - **BIZ UDGothic** (BIZUDGothic-Regular.ttf / BIZUDGothic-Bold.ttf)
  - **BIZ UDMincho** (BIZUDMincho-Regular.ttf)
- Copyright 2022 The BIZ UDGothic / BIZ UDMincho Project Authors
  (https://github.com/googlefonts/morisawa-biz-ud-gothic,
   https://github.com/googlefonts/morisawa-biz-ud-mincho)
- ライセンス: SIL Open Font License 1.1（`src/J2P.Core/Pdf/Fonts/OFL.txt`）

## NuGet 依存パッケージ

- [PDFsharp](https://www.pdfsharp.net/) — MIT License
- [System.Text.Encoding.CodePages](https://github.com/dotnet/runtime) — MIT License
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MIT License
- [WPF-UI](https://github.com/lepoco/wpfui) — MIT License

## 商標

「Jw_cad」は Jiro Shimizu 氏・Yoshifumi Tanaka 氏による CAD ソフトウェアの名称です。
本プロジェクトは Jw_cad と無関係の非公式ツールです。
