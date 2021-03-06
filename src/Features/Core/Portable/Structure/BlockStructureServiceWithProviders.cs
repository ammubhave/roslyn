﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.PooledObjects;

namespace Microsoft.CodeAnalysis.Structure
{
    internal abstract class BlockStructureServiceWithProviders : BlockStructureService
    {
        private readonly Workspace _workspace;
        private readonly ImmutableArray<BlockStructureProvider> _providers;

        protected BlockStructureServiceWithProviders(Workspace workspace)
        {
            _workspace = workspace;
            _providers = GetBuiltInProviders().Concat(GetImportedProviders());
        }

        /// <summary>
        /// Returns the providers always available to the service.
        /// This does not included providers imported via MEF composition.
        /// </summary>
        protected virtual ImmutableArray<BlockStructureProvider> GetBuiltInProviders()
            => ImmutableArray<BlockStructureProvider>.Empty;

        private ImmutableArray<BlockStructureProvider> GetImportedProviders()
        {
            var language = Language;
            var mefExporter = (IMefHostExportProvider)_workspace.Services.HostServices;

            var providers = mefExporter.GetExports<BlockStructureProvider, LanguageMetadata>()
                                       .Where(lz => lz.Metadata.Language == language)
                                       .Select(lz => lz.Value);

            return providers.ToImmutableArray();
        }

        public override async Task<BlockStructure> GetBlockStructureAsync(
            Document document, CancellationToken cancellationToken)
        {
            var context = new BlockStructureContext(document, cancellationToken);
            foreach (var provider in _providers)
            {
                await provider.ProvideBlockStructureAsync(context).ConfigureAwait(false);
            }

            return CreateBlockStructure(context);
        }

        public override BlockStructure GetBlockStructure(
            Document document, CancellationToken cancellationToken)
        {
            var context = new BlockStructureContext(document, cancellationToken);
            foreach (var provider in _providers)
            {
                provider.ProvideBlockStructure(context);
            }

            return CreateBlockStructure(context);
        }

        private static BlockStructure CreateBlockStructure(BlockStructureContext context)
        {
            var options = context.Document.Project.Solution.Workspace.Options;
            var language = context.Document.Project.Language;

            var showIndentGuidesForCodeLevelConstructs = options.GetOption(BlockStructureOptions.ShowBlockStructureGuidesForCodeLevelConstructs, language);
            var showIndentGuidesForDeclarationLevelConstructs = options.GetOption(BlockStructureOptions.ShowBlockStructureGuidesForDeclarationLevelConstructs, language);
            var showIndentGuidesForCommentsAndPreprocessorRegions = options.GetOption(BlockStructureOptions.ShowBlockStructureGuidesForCommentsAndPreprocessorRegions, language);
            var showOutliningForCodeLevelConstructs = options.GetOption(BlockStructureOptions.ShowOutliningForCodeLevelConstructs, language);
            var showOutliningForDeclarationLevelConstructs = options.GetOption(BlockStructureOptions.ShowOutliningForDeclarationLevelConstructs, language);
            var showOutliningForCommentsAndPreprocessorRegions = options.GetOption(BlockStructureOptions.ShowOutliningForCommentsAndPreprocessorRegions, language);

            using var _ = ArrayBuilder<BlockSpan>.GetInstance(out var updatedSpans);
            foreach (var span in context.Spans)
            {
                var updatedSpan = UpdateBlockSpan(span,
                    showIndentGuidesForCodeLevelConstructs,
                    showIndentGuidesForDeclarationLevelConstructs,
                    showIndentGuidesForCommentsAndPreprocessorRegions,
                    showOutliningForCodeLevelConstructs,
                    showOutliningForDeclarationLevelConstructs,
                    showOutliningForCommentsAndPreprocessorRegions);
                updatedSpans.Add(updatedSpan);
            }

            return new BlockStructure(updatedSpans.ToImmutable());
        }

        private static BlockSpan UpdateBlockSpan(BlockSpan blockSpan,
            bool showIndentGuidesForCodeLevelConstructs,
            bool showIndentGuidesForDeclarationLevelConstructs,
            bool showIndentGuidesForCommentsAndPreprocessorRegions,
            bool showOutliningForCodeLevelConstructs,
            bool showOutliningForDeclarationLevelConstructs,
            bool showOutliningForCommentsAndPreprocessorRegions)
        {
            var type = blockSpan.Type;

            var isTopLevel = BlockTypes.IsDeclarationLevelConstruct(type);
            var isMemberLevel = BlockTypes.IsCodeLevelConstruct(type);
            var isComment = BlockTypes.IsCommentOrPreprocessorRegion(type);

            if ((!showIndentGuidesForDeclarationLevelConstructs && isTopLevel) ||
                (!showIndentGuidesForCodeLevelConstructs && isMemberLevel) ||
                (!showIndentGuidesForCommentsAndPreprocessorRegions && isComment))
            {
                type = BlockTypes.Nonstructural;
            }

            var isCollapsible = blockSpan.IsCollapsible;
            if (isCollapsible)
            {
                if ((!showOutliningForDeclarationLevelConstructs && isTopLevel) ||
                    (!showOutliningForCodeLevelConstructs && isMemberLevel) ||
                    (!showOutliningForCommentsAndPreprocessorRegions && isComment))
                {
                    isCollapsible = false;
                }
            }

            return blockSpan.With(type: type, isCollapsible: isCollapsible);
        }
    }
}
