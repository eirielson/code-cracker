﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeCracker.CSharp.Performance
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MakeLocalVariableConstWhenItIsPossibleCodeFixProvider)), Shared]

    public class MakeLocalVariableConstWhenItIsPossibleCodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds =>
            ImmutableArray.Create(DiagnosticId.MakeLocalVariableConstWhenItIsPossible.ToDiagnosticId());

        public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;
            var localDeclaration = root.FindToken(diagnosticSpan.Start).Parent.AncestorsAndSelf().OfType<LocalDeclarationStatementSyntax>().First();
            const string message = "Make constant";
            context.RegisterCodeFix(CodeAction.Create(message, c => MakeConstantAsync(context.Document, localDeclaration, c)), diagnostic);
        }

        private async Task<Document> MakeConstantAsync(Document document, LocalDeclarationStatementSyntax localDeclaration, CancellationToken cancellationToken)
        {
            var declaration = localDeclaration.Declaration;
            var typeName = declaration.Type;

            if (typeName.IsVar)
            {
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

                var aliasInfo = semanticModel.GetAliasInfo(typeName);
                if (aliasInfo == null)
                {
                    var type = semanticModel.GetTypeInfo(typeName).ConvertedType;
                    if (type.Name != "var")
                    {
                        var newtypeName = SyntaxFactory.ParseTypeName(type.ToDisplayString());
                        declaration = declaration.WithType(newtypeName);
                    }
                }
            }

            var @const = SyntaxFactory.Token(SyntaxKind.ConstKeyword)
                .WithLeadingTrivia(localDeclaration.GetLeadingTrivia());

            var modifiers = localDeclaration.Modifiers.Insert(0, @const);

            var newLocalDeclaration = localDeclaration
                .WithModifiers(modifiers)
                .WithDeclaration(declaration.WithoutLeadingTrivia())
                .WithTrailingTrivia(localDeclaration.GetTrailingTrivia())
                .WithAdditionalAnnotations(Formatter.Annotation);

            var root = await document.GetSyntaxRootAsync(cancellationToken);
            var newRoot = root.ReplaceNode(localDeclaration, newLocalDeclaration);
            return document.WithSyntaxRoot(newRoot);
        }
    }
}