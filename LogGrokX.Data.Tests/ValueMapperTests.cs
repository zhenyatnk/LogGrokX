using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Data.Tests;

[TestClass]
public class ValueMapperTests
{
    private static ValueMappingList List(string name, params (string value, string mapping)[] items) => new()
    {
        Name = name,
        Items = Array.ConvertAll(items, i => new ValueMappingItem { Value = i.value, Mapping = i.mapping })
    };

    [TestMethod]
    public void MapsWholeValueInFormatValueWithMappingInParentheses()
    {
        var mapper = new ValueMapper(new[] { List("types", ("0x00001", "type 1")) });

        Assert.AreEqual("0x00001(type 1)", mapper.ForField("Type").Map("0x00001"));
    }

    [TestMethod]
    public void MapsEveryOccurrenceInsideText()
    {
        var mapper = new ValueMapper(new[] { List("types", ("0x00001", "type 1"), ("0x00002", "type 2")) });

        Assert.AreEqual("got 0x00001(type 1), then 0x00002(type 2) and 0x00001(type 1)",
            mapper.ForField("Message").Map("got 0x00001, then 0x00002 and 0x00001"));
    }

    [TestMethod]
    [DataRow("0x000010")]
    [DataRow("10x00001")]
    [DataRow("0x00001a")]
    [DataRow("_0x00001")]
    public void DoesNotMapPartOfLongerToken(string text)
    {
        var mapper = new ValueMapper(new[] { List("types", ("0x00001", "type 1")) });

        Assert.AreEqual(text, mapper.ForField(null).Map(text));
    }

    [TestMethod]
    public void MapsValueSurroundedByPunctuation()
    {
        var mapper = new ValueMapper(new[] { List("codes", ("5", "denied")) });

        Assert.AreEqual("code=5(denied); [5(denied)] \"5(denied)\"",
            mapper.ForField(null).Map("code=5; [5] \"5\""));
    }

    [TestMethod]
    public void ValueStartingWithPunctuationMatchesAfterWordCharacter()
    {
        var mapper = new ValueMapper(new[] { List("codes", ("-1", "invalid")) });

        Assert.AreEqual("rc-1(invalid)", mapper.ForField(null).Map("rc-1"));
    }

    [TestMethod]
    public void LongestValueWinsAtSamePosition()
    {
        var mapper = new ValueMapper(new[] { List("codes", ("ERR", "error"), ("ERR 5", "denied")) });

        Assert.AreEqual("ERR 5(denied) ERR(error)", mapper.ForField(null).Map("ERR 5 ERR"));
    }

    [TestMethod]
    public void IsCaseSensitiveByDefault()
    {
        var mapper = new ValueMapper(new[] { List("hr", ("0x8007000E", "E_OUTOFMEMORY")) });

        Assert.AreEqual("0x8007000e", mapper.ForField(null).Map("0x8007000e"));
    }

    [TestMethod]
    public void IgnoreCaseMatchesAndKeepsOriginalValue()
    {
        var list = List("hr", ("0x8007000E", "E_OUTOFMEMORY"));
        list.IgnoreCase = true;
        var mapper = new ValueMapper(new[] { list });

        Assert.AreEqual("0x8007000e(E_OUTOFMEMORY)", mapper.ForField(null).Map("0x8007000e"));
    }

    [TestMethod]
    public void FieldsRestrictWhereListApplies()
    {
        var list = List("levels", ("1", "info"));
        list.Fields = new[] { "Level" };
        var mapper = new ValueMapper(new[] { list });

        Assert.AreEqual("1(info)", mapper.ForField("Level").Map("1"));
        Assert.AreEqual("1(info)", mapper.ForField("level").Map("1"));
        Assert.AreEqual("1", mapper.ForField("Message").Map("1"));
        Assert.IsTrue(mapper.ForField(null).IsEmpty);
    }

    [TestMethod]
    public void DisabledListIsIgnored()
    {
        var list = List("types", ("0x00001", "type 1"));
        list.Enabled = false;
        var mapper = new ValueMapper(new[] { list });

        Assert.IsTrue(mapper.IsEmpty);
        Assert.AreEqual("0x00001", mapper.ForField("Message").Map("0x00001"));
    }

    [TestMethod]
    public void FirstListWinsForSameValue()
    {
        var mapper = new ValueMapper(new[]
        {
            List("first", ("7", "seven")),
            List("second", ("7", "sieben"))
        });

        Assert.AreEqual("7(seven)", mapper.ForField(null).Map("7"));
    }

    [TestMethod]
    public void MappedTextIsNotMappedAgain()
    {
        var mapper = new ValueMapper(new[] { List("codes", ("1", "type 1")) });

        Assert.AreEqual("1(type 1) 2", mapper.ForField(null).Map("1 2"));
    }

    [TestMethod]
    public void ValuesAreTrimmedAndEmptyItemsSkipped()
    {
        var mapper = new ValueMapper(new[] { List("codes", (" 42 ", "answer"), ("", "nothing"), ("  ", "blank")) });

        Assert.AreEqual("42(answer)", mapper.ForField(null).Map("42"));
    }

    [TestMethod]
    public void ValueWithRegexCharactersIsMatchedLiterally()
    {
        var mapper = new ValueMapper(new[] { List("codes", ("a.b(c)", "literal")) });

        Assert.AreEqual("a.b(c)(literal) axb(c)", mapper.ForField(null).Map("a.b(c) axb(c)"));
    }

    [TestMethod]
    public void ForFieldsReturnsMapperPerFieldAndCachesArray()
    {
        var list = List("levels", ("1", "info"));
        list.Fields = new[] { "Level" };
        var mapper = new ValueMapper(new[] { list });
        var names = new[] { "Time", "Level" };

        var mappers = mapper.ForFields(names);

        Assert.AreEqual(2, mappers.Length);
        Assert.IsTrue(mappers[0].IsEmpty);
        Assert.AreEqual("1(info)", mappers[1].Map("1"));
        Assert.AreSame(mappers, mapper.ForFields(new[] { "Time", "Level" }));
    }

    [TestMethod]
    public void FindMatchesInRangeReturnsOnlyMatchesInsideRange()
    {
        var mapper = new ValueMapper(new[] { List("codes", ("1", "one")) });

        var matches = mapper.ForField(null).FindMatches("1 1 1", 2, 1);

        Assert.AreEqual(1, matches.Count);
        Assert.AreEqual(new ValueMappingMatch(2, 1, "one"), matches[0]);
    }

    [TestMethod]
    public void EmptyMapperLeavesTextUnchanged()
    {
        Assert.AreEqual("0x00001", ValueMapper.Empty.ForField("Message").Map("0x00001"));
        Assert.IsTrue(FieldValueMapper.None.IsEmpty);
    }
}
