﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.Collections.Generic;
using System.Linq;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.PatternMatching;

namespace ICSharpCode.Decompiler.Ast.Transforms
{
	/// <summary>
	/// Description of DecimalConstantTransform.
	/// </summary>
	public class DecimalConstantTransform : DepthFirstAstVisitor<object, object>, IAstTransform
	{
		static readonly ICSharpCode.NRefactory.CSharp.Attribute decimalConstantAttribute = new ICSharpCode.NRefactory.CSharp.Attribute {
			Type = new SimpleType("DecimalConstant"),
			Arguments = { new Repeat(new AnyNode()) }
		};
		
		static readonly FieldDeclaration decimalConstantPattern = new FieldDeclaration {
			Attributes = {
				new AttributeSection {
					Attributes = {
						decimalConstantAttribute
					}
				}
			},
			Modifiers = Modifiers.Any,
			Variables = { new Repeat(new AnyNode()) },
			ReturnType = new PrimitiveType("decimal")
		};
		
		Expression ConstructExpression(string typeName, string member)
		{
			return new AssignmentExpression(
				new MemberReferenceExpression(
					new TypeReferenceExpression(new SimpleType(typeName)),
					member
				),
				AssignmentOperatorType.Assign,
				new AnyNode()
			);
		}
		
		class ClassInfo
		{
			public ClassInfo()
			{
				Fields = new List<string>();
			}
			
			public List<string> Fields { get; private set; }
			public TypeDeclaration Declaration { get; set; }
		}
		
		Stack<ClassInfo> replaceableFields;
		
		public DecimalConstantTransform()
		{
		}
		
		void IAstTransform.Run(AstNode compilationUnit)
		{
			this.replaceableFields = new Stack<ClassInfo>();
			compilationUnit.AcceptVisitor(this, null);
		}
		
		public override object VisitFieldDeclaration(FieldDeclaration fieldDeclaration, object data)
		{
			base.VisitFieldDeclaration(fieldDeclaration, data);
			
			var match = decimalConstantPattern.Match(fieldDeclaration);
			
			if (match.Success) {
				Modifiers pattern = Modifiers.Static | Modifiers.Readonly;
				if ((fieldDeclaration.Modifiers & pattern) == pattern) {
					replaceableFields.Peek().Fields.AddRange(fieldDeclaration.Variables.Select(v => v.Name));
					fieldDeclaration.ReplaceWith(ReplaceFieldWithConstant);
				}
			}
			
			return null;
		}
		
		AstNode ReplaceFieldWithConstant(AstNode node)
		{
			var old = node as FieldDeclaration;
			var fd = new FieldDeclaration {
				Modifiers = old.Modifiers & ~(Modifiers.Readonly | Modifiers.Static) | Modifiers.Const,
				ReturnType = new PrimitiveType("decimal")
			};
			
			var foundAttr = old.Attributes.SelectMany(section => section.Attributes)
				.First(a => decimalConstantAttribute.IsMatch(a));
			foundAttr.Remove();
			foreach (var attr in old.Attributes.Where(section => section.Attributes.Count == 0))
				attr.Remove();
			
			old.Attributes.MoveTo(fd.Attributes);
			old.Variables.MoveTo(fd.Variables);
			
			fd.Variables.Single().Initializer = new PrimitiveExpression(CreateDecimalValue(foundAttr));
			
			return fd;
		}
		
		object CreateDecimalValue(ICSharpCode.NRefactory.CSharp.Attribute foundAttr)
		{
			byte scale = (byte)((PrimitiveExpression)foundAttr.Arguments.ElementAt(0)).Value;
			byte sign = (byte)((PrimitiveExpression)foundAttr.Arguments.ElementAt(1)).Value;
			int high = (int)(uint)((PrimitiveExpression)foundAttr.Arguments.ElementAt(2)).Value;
			int mid = (int)(uint)((PrimitiveExpression)foundAttr.Arguments.ElementAt(3)).Value;
			int low = (int)(uint)((PrimitiveExpression)foundAttr.Arguments.ElementAt(4)).Value;
			
			return new Decimal(low, mid, high, sign == 1, scale);
		}
		
		public override object VisitConstructorDeclaration(ConstructorDeclaration constructorDeclaration, object data)
		{
			if ((constructorDeclaration.Modifiers & Modifiers.Static) == Modifiers.Static && replaceableFields.Count > 0) {
				var current = replaceableFields.Peek();
				
				foreach (var fieldName in current.Fields) {
					var pattern = ConstructExpression(current.Declaration.Name, fieldName);
					foreach (var expr in constructorDeclaration.Body
					         .OfType<ExpressionStatement>()) {
						if (pattern.IsMatch(expr.Expression))
							expr.Remove();
					}
				}
			}
			
			return null;
		}
		
		public override object VisitTypeDeclaration(TypeDeclaration typeDeclaration, object data)
		{
			replaceableFields.Push(new ClassInfo() { Declaration = typeDeclaration });
			base.VisitTypeDeclaration(typeDeclaration, data);
			replaceableFields.Pop();
			return null;
		}
	}
}