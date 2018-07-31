﻿using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DisposableFixer.CodeFix.Extensions;
using DisposableFixer.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace DisposableFixer.CodeFix
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UndisposedMemberCodeFixProvider))]
    [Shared]
    public class IntroduceFieldAndDisposeInDisposeMethodCodeFixProvider : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(
            SyntaxNodeAnalysisContextExtension.IdForAnonymousObjectFromMethodInvocation,
            SyntaxNodeAnalysisContextExtension.IdForAnonymousObjectFromObjectCreation,
            SyntaxNodeAnalysisContextExtension.IdForNotDisposedLocalVariable
        );

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var diagnotic = context.Diagnostics.FirstOrDefault();
            if (diagnotic == null) return Task.CompletedTask;

            context.RegisterCodeFix(
                IsUndisposedLocalVariable(context)
                    ? CodeAction.Create("Create field and dispose in Dispose() method.",
                        cancel => DisposeInDisposeMethod(context, cancel))
                    : CodeAction.Create("Create field and dispose in Dispose() method.",
                        cancel => IntroduceFieldAndDisposeInDisposeMethod(context, cancel)),
                diagnotic
            );

            return Task.CompletedTask;
        }

        private static async Task<Document> DisposeInDisposeMethod(CodeFixContext context, CancellationToken cancel)
        {
            var editor = await DocumentEditor.CreateAsync(context.Document, cancel);
            var node = editor.OriginalRoot.FindNode(context.Span);
            var fieldName = RetrieveFieldName(context, node);
            var model = editor.SemanticModel;
            var type = model.GetTypeInfo(node).Type?.Name ?? Constants.IDisposable;

            if (!node.TryFindParent<ClassDeclarationSyntax>(out var oldClass)) return editor.GetChangedDocument();

            editor.AddBaseTypeIfNeeded(oldClass, SyntaxFactory.IdentifierName(Constants.IDisposable));
            editor.AddUninitializedFieldNamed(oldClass, fieldName, type);

            var assignment = SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.IdentifierName(fieldName),
                    node as ExpressionSyntax
                )
            );
            editor.ReplaceNode(node.Parent.Parent, assignment);

            var disposeMethods = oldClass.GetParameterlessMethodNamed(Constants.Dispose).ToArray();

            if (disposeMethods.Any())
                editor.AddDisposeCallToMemberInDisposeMethod(disposeMethods.First(), fieldName);
            else
                editor.AddDisposeMethodAndDisposeCallToMember(oldClass, fieldName);

            editor.AddImportIfNeeded(Constants.System);

            return editor.GetChangedDocument();
        }

        private static async Task<Document> IntroduceFieldAndDisposeInDisposeMethod(CodeFixContext context,
            CancellationToken cancel)
        {
            var editor = await DocumentEditor.CreateAsync(context.Document, cancel);
            var node = editor.OriginalRoot.FindNode(context.Span);
            var fieldName = RetrieveFieldName(context, node);
            var model = editor.SemanticModel;


            if (!node.TryFindParent<ClassDeclarationSyntax>(out var oldClass)) return editor.GetChangedDocument();

            editor.AddBaseTypeIfNeeded(oldClass, SyntaxFactory.IdentifierName(Constants.IDisposable));


            switch (node)
            {
                case ExpressionSyntax expression:
                    ReplaceExpression(model, node, editor, oldClass, fieldName, expression);
                    break;
                case ArgumentSyntax argument:
                    ReplaceArgument(model, argument, editor, oldClass, fieldName, node);
                    break;
                default:
                    throw new NotSupportedException($"Cannot wrap type '{node.GetType().FullName}'");
            }

            var disposeMethods = oldClass.GetParameterlessMethodNamed(Constants.Dispose)
                .ToArray();

            if (disposeMethods.Any())
                editor.AddDisposeCallToMemberInDisposeMethod(disposeMethods.First(), fieldName);
            else
                editor.AddDisposeMethodAndDisposeCallToMember(oldClass, fieldName);

            editor.AddImportIfNeeded(Constants.System);

            return editor.GetChangedDocument();
        }

        private static void ReplaceArgument(SemanticModel model, ArgumentSyntax argumentSyntax, DocumentEditor editor,
            ClassDeclarationSyntax oldClass, string fieldName, SyntaxNode node)
        {
            var typeInfo = model.GetTypeInfo(argumentSyntax.Expression);
            var type = typeInfo.Type?.Name ?? Constants.IDisposable;
            editor.AddUninitializedFieldNamed(oldClass, fieldName, type);
            if (!node.TryFindContainigBlock(out var block)) return;

            var assignmentExpresion = SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.IdentifierName(fieldName),
                    argumentSyntax.Expression
                )
            );
            var preceedingStatements =
                block.Statements.TakeWhile(ss =>
                    ss.DescendantNodes<ArgumentSyntax>().All(@as => @as != argumentSyntax));

            var variable = SyntaxFactory.Argument(SyntaxFactory.IdentifierName(fieldName));
            var currentStatement = block.Statements
                .SkipWhile(ss => ss.DescendantNodes<ArgumentSyntax>().All(@as => @as != argumentSyntax))
                .FirstOrDefault()
                .ReplaceNode(node, variable);
            var trailingStatements = block.Statements
                .SkipWhile(ss => ss.DescendantNodes<ArgumentSyntax>().All(@as => @as != argumentSyntax))
                .Skip(1);
            var newBlock = SyntaxFactory.Block(preceedingStatements
                .Concat(assignmentExpresion)
                .Concat(currentStatement)
                .Concat(trailingStatements));

            editor.ReplaceNode(block, newBlock);
        }

        private static void ReplaceExpression(SemanticModel model, SyntaxNode node, DocumentEditor editor,
            ClassDeclarationSyntax oldClass, string fieldName, ExpressionSyntax expressionSyntax)
        {
            var typeInfo = model.GetTypeInfo(node);
            var type = typeInfo.Type?.Name ?? Constants.IDisposable;
            editor.AddUninitializedFieldNamed(oldClass, fieldName, type);
            var assignment = SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.IdentifierName(fieldName),
                    expressionSyntax
                )
            );
            editor.ReplaceNode(node.Parent, assignment);
        }

        private static string RetrieveFieldName(CodeFixContext context, SyntaxNode node)
        {
            const string defaultName = "_disposable";
            if (!IsUndisposedLocalVariable(context)) return defaultName;

            var variableDeclarator = node.Parent?.Parent as VariableDeclaratorSyntax;
            return variableDeclarator?.Identifier.Text ?? defaultName;
        }

        private static bool IsUndisposedLocalVariable(CodeFixContext context)
        {
            return context.Diagnostics.First().Id ==
                   SyntaxNodeAnalysisContextExtension.IdForNotDisposedLocalVariable;
        }
    }
}