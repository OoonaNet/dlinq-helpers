﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic;
using System.Linq.Expressions;
using DynamicExpression = System.Linq.Dynamic.DynamicExpression;

namespace Kendo.DynamicLinq
{
	public static class QueryableExtensions
	{
		/// <summary>
		/// Applies data processing (paging, sorting, filtering and aggregates) over IQueryable using Dynamic Linq.
		/// </summary>
		/// <typeparam name="T">The type of the IQueryable.</typeparam>
		/// <param name="queryable">The IQueryable which should be processed.</param>
		/// <param name="take">Specifies how many items to take. Configurable via the pageSize setting of the Kendo DataSource.</param>
		/// <param name="skip">Specifies how many items to skip.</param>
		/// <param name="sort">Specifies the current sort order.</param>
		/// <param name="filter">Specifies the current filter.</param>
		/// <param name="aggregates">Specifies the current aggregates.</param>
		/// <returns>A DataSourceResult object populated from the processed IQueryable.</returns>
		public static DataSourceResult ToDataSourceResult<T>(this IQueryable<T> queryable, int take, int skip, IEnumerable<Sort> sort, Filter filter, IEnumerable<Aggregator> aggregates)
		{
			// Filter the data first
			queryable = Filter(queryable, filter);

			// Calculate the total number of records (needed for paging)
			var total = queryable.Count();

			// Calculate the aggregates
			var aggregate = Aggregate(queryable, aggregates);

			// Sort the data
			queryable = Sort(queryable, sort);

			// Finally page the data
			if (take > 0)
            {
				queryable = Page(queryable, take, skip);
			}

			return new DataSourceResult
			{
				Data = queryable.ToList(),
				Total = total,
				Aggregates = aggregate
			};
		}

		/// <summary>
		/// Applies data processing (paging, sorting and filtering) over IQueryable using Dynamic Linq.
		/// </summary>
		/// <typeparam name="T">The type of the IQueryable.</typeparam>
		/// <param name="queryable">The IQueryable which should be processed.</param>
		/// <param name="take">Specifies how many items to take. Configurable via the pageSize setting of the Kendo DataSource.</param>
		/// <param name="skip">Specifies how many items to skip.</param>
		/// <param name="sort">Specifies the current sort order.</param>
		/// <param name="filter">Specifies the current filter.</param>
		/// <returns>A DataSourceResult object populated from the processed IQueryable.</returns>
		public static DataSourceResult ToDataSourceResult<T>(this IQueryable<T> queryable, int take, int skip, IEnumerable<Sort> sort, Filter filter)
		{
			return queryable.ToDataSourceResult(take, skip, sort, filter, null);
		}

        /// <summary>
        ///  Applies data processing (paging, sorting and filtering) over IQueryable using Dynamic Linq.
        /// </summary>
        /// <typeparam name="T">The type of the IQueryable.</typeparam>
        /// <param name="queryable">The IQueryable which should be processed.</param>
        /// <param name="request">The DataSourceRequest object containing take, skip, order, and filter data.</param>
        /// <returns>A DataSourceResult object populated from the processed IQueryable.</returns>
	    public static DataSourceResult ToDataSourceResult<T>(this IQueryable<T> queryable, DataSourceRequest request)
	    {
	        return queryable.ToDataSourceResult(request.Take, request.Skip, request.Sort, request.Filter, null);
	    }

        private static IQueryable<T> Filter<T>(IQueryable<T> queryable, Filter filter)
        {
            if ((filter != null) && (filter.Logic != null))
            {
                // Collect a flat list of all filters
                var filters = filter.All();

                // Get all filter values as array (needed by the Where method of Dynamic Linq)
                var values = filters.Select(f => f.Value is string ? f.Value.ToString().ToLower() : f.Value).ToArray();

                ////Add toLower() for all filter Fields with type of string in the values
                for (var i = 0; i < values.Length; i++)
                {
                    try
                    {
                        var propertyType = typeof(T).GetProperty(filters[i].Field).PropertyType;

                        if (values[i] is string)
                        {
                            if(Nullable.GetUnderlyingType(propertyType) != typeof(Guid))
                            {
                                filters[i].Field = string.Format("{0}.ToString().ToLower()", filters[i].Field);
                            }
                        }
                        // when we have a decimal value it gets converted to double and the query will break
                        if (values[i] is double)
                        {
                            values[i] = Convert.ToDecimal(values[i]);
                        }
                        if (values[i] is DateTime)
                        {
                            var dateTimeFilterValue = (DateTime)values[i];
                            values[i] = new DateTime(dateTimeFilterValue.Year, dateTimeFilterValue.Month,
                                dateTimeFilterValue.Day, 0, 0, 0);
                        }
                        if (Nullable.GetUnderlyingType(propertyType) == typeof(Guid) || propertyType == typeof(Guid))
                        {
                            values[i] = Guid.Parse(values[i].ToString());
                        }
                    }
                    catch (Exception ex)
                    {
                        filters[i].Field = string.Format("{0}.ToString().ToLower()", filters[i].Field);
                    }
                }

                var valuesList = values.ToList();

                //Remove duplicate filters
                //NOTE: we loop, and don't use .distinct for a reason!
                //There is a minuscule chance different columns will filter by the same value, in which case using distinct will remove too many filters
                for (int i = filters.Count - 1; i >= 0; i--)
                {
                    var previousFilter = filters.ElementAtOrDefault(i - 1);

                    if (previousFilter != null && filters[i].Equals(previousFilter))
                    {
                        filters.RemoveAt(i);

                        valuesList.RemoveAt(i);
                    }
                }
                var filtersList = filters.ToList();
                for (int i = 0; i < filters.Count; i++)
                {
                    if (filters[i].Value is DateTime && filters[i].Operator == "eq")
                    {
                        var filterToEdit = filtersList[i];

                        //Copy the date from the filter
                        var baseDate = ((DateTime)filters[i].Value).Date;

                        //Instead of comparing for exact equality, we compare as greater than the start of the day...
                        filterToEdit.Value = new DateTime(baseDate.Year, baseDate.Month, baseDate.Day, 0, 0, 0);
                        filterToEdit.Operator = "gte";
                        valuesList[i] = filterToEdit.Value;

                        //...and less than the end of that same day (we're making an additional filter here)
                        var newFilter = new Filter()
                        {
                            Value = new DateTime(baseDate.Year, baseDate.Month, baseDate.Day, 23, 59, 59),
                            Field = filters[i].Field,
                            Filters = filters[i].Filters,
                            Operator = "lte",
                            Logic = "and"
                        };

                        //Add that additional filter to the list of filters
                        filtersList.Add(newFilter);
                        valuesList.Add(newFilter.Value);
                    }
                }

                values = valuesList.ToArray();
                filters = filtersList;
                //Set the filters, since we may have edited them
                filter.Filters = filtersList;

                // Create a predicate expression e.g. Field1 = @0 And Field2 > @1
                var predicate = filter.ToExpression(filters);

                // Use the Where method of Dynamic Linq to filter the data
                queryable = queryable.Where(predicate, values);
            }

            return queryable;
        }

        private static object Aggregate<T>(IQueryable<T> queryable, IEnumerable<Aggregator> aggregates)
		{
			if (aggregates != null && aggregates.Any())
			{
				var objProps = new Dictionary<DynamicProperty, object>();
				var groups = aggregates.GroupBy(g => g.Field);
				Type type = null;
				foreach (var group in groups)
				{
					var fieldProps = new Dictionary<DynamicProperty, object>();
					foreach (var aggregate in group)
					{
						var prop = typeof (T).GetProperty(aggregate.Field);
						var param = Expression.Parameter(typeof (T), "s");
						var selector = aggregate.Aggregate == "count" && (Nullable.GetUnderlyingType(prop.PropertyType) != null)
							? Expression.Lambda(Expression.NotEqual(Expression.MakeMemberAccess(param, prop), Expression.Constant(null, prop.PropertyType)), param)
							: Expression.Lambda(Expression.MakeMemberAccess(param, prop), param);
						var mi = aggregate.MethodInfo(typeof (T));
						if (mi == null)
							continue;

						var val = queryable.Provider.Execute(Expression.Call(null, mi,
							aggregate.Aggregate == "count" && (Nullable.GetUnderlyingType(prop.PropertyType) == null)
								? new[] { queryable.Expression }
								: new[] { queryable.Expression, Expression.Quote(selector) }));

						fieldProps.Add(new DynamicProperty(aggregate.Aggregate, typeof(object)), val);
					}
					type = DynamicExpression.CreateClass(fieldProps.Keys);
					var fieldObj = Activator.CreateInstance(type);
					foreach (var p in fieldProps.Keys)
						type.GetProperty(p.Name).SetValue(fieldObj, fieldProps[p], null);
					objProps.Add(new DynamicProperty(group.Key, fieldObj.GetType()), fieldObj);
				}

				type = DynamicExpression.CreateClass(objProps.Keys);

				var obj = Activator.CreateInstance(type);

				foreach (var p in objProps.Keys)
                {
					type.GetProperty(p.Name).SetValue(obj, objProps[p], null);
                }

				return obj;
			}
            else
            {
                return null;
            }
		}

		private static IQueryable<T> Sort<T>(IQueryable<T> queryable, IEnumerable<Sort> sort)
		{
			if (sort != null && sort.Any())
			{
				// Create ordering expression e.g. Field1 asc, Field2 desc
				var ordering = String.Join(",", sort.Select(s => s.ToExpression()));

				// Use the OrderBy method of Dynamic Linq to sort the data
				return queryable.OrderBy(ordering);
			}

			return queryable;
		}

		private static IQueryable<T> Page<T>(IQueryable<T> queryable, int take, int skip)
		{
			return queryable.Skip(skip).Take(take);
		}
	}
}
