using System.Collections.Generic;
using AiAgent.Backend.Services.ChangeAtlas;
using Xunit;
namespace AiAgent.Backend.Tests;
public class AtlasImportsTests
{
    [Fact]
    public void RelativeResolutionRejectsEscapeAndAmbiguity()
    {
        var paths = new HashSet<string> { "src/b.ts", "src/index.ts", "src/b.tsx" };
        Assert.Null(ChangeAtlasService.ResolveImport("src/a.ts", "../../b", paths));
        Assert.Null(ChangeAtlasService.ResolveImport("src/a.ts", "./b", paths));
        Assert.Equal("src/index.ts", ChangeAtlasService.ResolveImport("src/a.ts", "./index", paths));
    }
    [Fact]
    public void ImportCandidatesKeepLineEvidenceAndSkipSimpleComments()
    {
        var imports = ChangeAtlasService.ImportStatements("// import './bad';\nimport {x} from './b';\n/*\nimport './hidden';\n*/\nexport * from '../shared';\nimport 'external';");
        Assert.Equal(2, imports.Count);
        Assert.Equal(("./b", 2, "import {x} from './b';"), imports[0]);
        Assert.Equal("../shared", imports[1].Specifier);
    }
}
