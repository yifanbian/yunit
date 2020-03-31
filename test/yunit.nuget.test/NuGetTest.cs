﻿using System;
using System.IO;
using System.Threading.Tasks;

namespace Yunit.NuGetTest
{
    public class NuGetTest
    {
        [MarkdownTest("~/test/yunit.nuget.test/**/*.md")]
        public void Foo(string filename)
        {
            File.WriteAllText(filename, "");
        }

        [MarkdownTest("~/test/yunit.nuget.test/**/*.md")]
        public void SkipSync(string filename)
        {
            throw new TestSkippedException();
        }

        [MarkdownTest("~/test/yunit.nuget.test/**/*.md")]
        public async Task SkipAsync(string filename)
        {
            await Task.Delay(1);
            throw new TestSkippedException();
        }
    }
}