﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace RoslynGenerator
{
    public class ProxyGenerator
    {
        public ClassDeclarationSyntax GenerateProxy(ClassDeclarationSyntax classSyntax, SemanticModel model)
        {
            var classSemantic = model.GetDeclaredSymbol(classSyntax);
            var classDeclaration = ClassDeclaration($"{classSemantic.Name}Proxy");

            classDeclaration = classDeclaration.AddModifiers(classSyntax.Modifiers.ToArray())
                                               .WithBaseList(BaseList(SingletonSeparatedList<BaseTypeSyntax>(SimpleBaseType(IdentifierName(classSemantic.Name)))));

            var methodDeclaration = MethodDeclaration(ParseTypeName("Task"), "ReceiveAsync")
                                   .WithModifiers(TokenList(new[] { Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.AsyncKeyword), Token(SyntaxKind.OverrideKeyword) }))
                                   .AddParameterListParameters(Parameter(new SyntaxList<AttributeListSyntax>(), new SyntaxTokenList(), IdentifierName(Identifier("IContext")), Identifier("context"), null))
                                   .WithBody(Block(
                                       LocalDeclarationStatement(VariableDeclaration(IdentifierName("var")).WithVariables(SingletonSeparatedList(VariableDeclarator(Identifier("msg")).WithInitializer(EqualsValueClause(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("context"), IdentifierName("Message"))))))),
                                       GenerateActorStartedHandler(),
                                       GenerateCommandHandlers(classSyntax, model),
                                       GenerateQueryHandlers(classSyntax, model),
                                       GetUnsupportedThrow())
                                    );

            classDeclaration = classDeclaration.AddMembers(methodDeclaration);

            return classDeclaration;
        }

        private StatementSyntax GenerateActorStartedHandler()
        {
            return IfStatement(BinaryExpression(SyntaxKind.IsExpression, IdentifierName("msg"), IdentifierName("Started")), ReturnStatement());
        }

        private StatementSyntax GenerateCommandHandlers(ClassDeclarationSyntax classSyntax, SemanticModel model)
        {
            int param_num = 0;

            var methods = GetMethodList(classSyntax, model, "Command");

            var ifStatement = GetIfCommand(ref param_num, methods[0]);

            foreach (var method in methods.Skip(1))
            {
                ifStatement = ifStatement.WithElse(ElseClause(GetIfCommand(ref param_num, method)));
            }

            return ifStatement;
        }

        private IfStatementSyntax GetIfCommand(ref int param_num, MethodDescription method)
        {
            return IfStatement(IsPatternExpression(IdentifierName("msg"), DeclarationPattern(IdentifierName(method.Parameter.Type.Name), SingleVariableDesignation(Identifier($"{"command_"}{param_num}")))), GetCommandInvocationBody(method.Method.Identifier.ValueText, $"{"command_"}{param_num++}"));
        }

        private StatementSyntax GenerateQueryHandlers(ClassDeclarationSyntax classSyntax, SemanticModel model)
        {
            int param_num = 0;
            var methods = GetMethodList(classSyntax, model, "Query");

            var ifStatement = GetIfQuery(ref param_num, methods[0]);

            foreach (var method in methods.Skip(1))
            {
                ifStatement = ifStatement.WithElse(ElseClause(GetIfQuery(ref param_num, method)));
            }

            return ifStatement;
        }

        private IfStatementSyntax GetIfQuery(ref int param_num, MethodDescription method)
        {
            return IfStatement(IsPatternExpression(IdentifierName("msg"), DeclarationPattern(IdentifierName(method.Parameter.Type.Name), SingleVariableDesignation(Identifier($"{"query_"}{param_num}")))), GetQueryInvocationBody(method.Method.Identifier.ValueText, $"{"query_"}{param_num++}"));
        }

        private static List<MethodDescription> GetMethodList(ClassDeclarationSyntax classSyntax, SemanticModel model, string parameterType)
        {
            return classSyntax.DescendantNodes()
                                     .OfType<MethodDeclarationSyntax>()
                                     .Where(m => m.ParameterList.Parameters.Count == 1)
                                     .Select(c => new MethodDescription { Method = c, Parameter = model.GetDeclaredSymbol(c.ParameterList.Parameters.FirstOrDefault()) })
                                     .Where(x => x.Parameter.Type.BaseType.Name == parameterType)
                                     .ToList();
        }

        private static StatementSyntax GetUnsupportedThrow()
        {
            return ThrowStatement(ObjectCreationExpression(IdentifierName("Exception")).WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(InterpolatedStringExpression(Token(SyntaxKind.InterpolatedStringStartToken)).WithContents(List(new InterpolatedStringContentSyntax[] { InterpolatedStringText().WithTextToken(Token(TriviaList(), SyntaxKind.InterpolatedStringTextToken, "Command type ", "Command type ", TriviaList())), Interpolation(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("msg"), IdentifierName("GetType"))), IdentifierName("Name"))), InterpolatedStringText().WithTextToken(Token(TriviaList(), SyntaxKind.InterpolatedStringTextToken, " is not supported", " is not supported", TriviaList())) })))))));
        }

        private StatementSyntax GetCommandInvocationBody(string handlerName, string paramName)
        {
            return Block(ExpressionStatement(AwaitExpression(InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, InvocationExpression(IdentifierName(handlerName)).WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(IdentifierName(paramName))))), IdentifierName("ConfigureAwait"))).WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(LiteralExpression(SyntaxKind.FalseLiteralExpression))))))), ReturnStatement());
        }

        private StatementSyntax GetQueryInvocationBody(string handlerName, string paramName)
        {
            return Block(LocalDeclarationStatement(VariableDeclaration(IdentifierName("var")).WithVariables(SingletonSeparatedList(VariableDeclarator(Identifier("result")).WithInitializer(EqualsValueClause(AwaitExpression(InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, InvocationExpression(IdentifierName(handlerName)).WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(IdentifierName(paramName))))), IdentifierName("ConfigureAwait"))).WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(LiteralExpression(SyntaxKind.FalseLiteralExpression))))))))))), ExpressionStatement(InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("context"), IdentifierName("Respond"))).WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(IdentifierName("result")))))), ReturnStatement());
        }

        private class MethodDescription
        {
            public MethodDeclarationSyntax Method { get; set; }
            public IParameterSymbol Parameter { get; set; }
        }
    }
}