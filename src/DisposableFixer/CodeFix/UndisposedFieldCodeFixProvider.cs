﻿using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DisposableFixer.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DisposableFixer.CodeFix
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UndisposedMemberCodeFixProvider)), Shared]
    public class UndisposedFieldCodeFixProvider : UndisposedMemberCodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            ImmutableArray.Create(
                SyntaxNodeAnalysisContextExtension.IdForAssignmendFromMethodInvocationToFieldNotDisposed,
                SyntaxNodeAnalysisContextExtension.IdForAssignmendFromObjectCreationToFieldNotDisposed
            );

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var id = context.Diagnostics.First().Id;
            if (id == SyntaxNodeAnalysisContextExtension.IdForAssignmendFromObjectCreationToFieldNotDisposed
                || id == SyntaxNodeAnalysisContextExtension.IdForAssignmendFromMethodInvocationToFieldNotDisposed)
            {
                context.RegisterCodeFix(
                    CodeAction.Create("Dispose field in Dispose() method", c => CreateDisposeCallInParameterlessDisposeMethod(context, c)),
                    context.Diagnostics);
            }
            return Task.FromResult(1);
        }

        protected override TypeSyntax GetTypeOfMemberDeclarationOrDefault(ClassDeclarationSyntax @class, string memberName)
        {
            return @class
                .DescendantNodes<FieldDeclarationSyntax>()
                .SelectMany(fds => fds.DescendantNodes<VariableDeclaratorSyntax>())
                    .Where(vds => vds.Identifier.Text == memberName)
                    .Select(vds => vds.TryFindParent<VariableDeclarationSyntax>(@class, out var variableDeclaration) ? variableDeclaration : null)
                    .Select(vd => vd?.Type)
                    .FirstOrDefault();
        }
    }
}