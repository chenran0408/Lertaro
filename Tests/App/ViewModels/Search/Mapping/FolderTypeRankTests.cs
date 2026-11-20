using System.IO;

using Lertaro.App.ViewModels.Search;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core;
using Lertaro.Core.SearchIndex;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

// Folders are their own entry in the result-type order, so "Applications, Files, Folders" can be said. Every
// file-backed source puts its rows on the Files rank first; these cases pin the pass that moves directories
// to the Folders rank and the trigger rule that splits the two buckets.
[TestClass]
public sealed class FolderTypeRankTests
{
    [TestMethod]
    public void ATypeTriggerKeepsOnlyTheFileIndexRowsOfItsOwnBucket()
    {
        Assert.IsTrue(SearchResultMapper.InTriggeredFileBucket(isDirectory: false, null));
        Assert.IsTrue(SearchResultMapper.InTriggeredFileBucket(isDirectory: true, null));
        Assert.IsTrue(SearchResultMapper.InTriggeredFileBucket(isDirectory: false, SearchResultTypePriority.FilesTypeId));
        Assert.IsFalse(SearchResultMapper.InTriggeredFileBucket(isDirectory: true, SearchResultTypePriority.FilesTypeId));
        Assert.IsTrue(SearchResultMapper.InTriggeredFileBucket(isDirectory: true, SearchResultTypePriority.FoldersTypeId));
        Assert.IsFalse(SearchResultMapper.InTriggeredFileBucket(isDirectory: false, SearchResultTypePriority.FoldersTypeId));
        Assert.IsFalse(SearchResultMapper.InTriggeredFileBucket(isDirectory: true, "apps"));
        Assert.IsFalse(SearchResultMapper.InTriggeredFileBucket(isDirectory: false, "apps"));
    }
}
