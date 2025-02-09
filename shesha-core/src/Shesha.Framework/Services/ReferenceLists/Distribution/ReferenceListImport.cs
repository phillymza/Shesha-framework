﻿using Abp.Dependency;
using Abp.Domain.Repositories;
using Abp.Domain.Uow;
using Newtonsoft.Json;
using Shesha.ConfigurationItems.Distribution;
using Shesha.Domain;
using Shesha.Domain.ConfigurationItems;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Shesha.Services.ReferenceLists.Distribution
{
    /// <summary>
    /// Reference list import
    /// </summary>
    public class ReferenceListImport: IReferenceListImport, ITransientDependency
    {
        private readonly IRepository<ReferenceList, Guid> _refListRepo;
        private readonly IRepository<ReferenceListItem, Guid> _refListItemRepo;
        private readonly IRepository<ConfigurationItem, Guid> _configItemRepository;
        private readonly IRepository<Module, Guid> _moduleRepo;
        private readonly IReferenceListManager _refListManger;
        private readonly IUnitOfWorkManager _unitOfWorkManager;

        public ReferenceListImport(IReferenceListManager formManger, IRepository<ReferenceList, Guid> refListRepo, IRepository<ReferenceListItem, Guid> refListItemRepo, IRepository<ConfigurationItem, Guid> configItemRepository, IRepository<Module, Guid> moduleRepo, IUnitOfWorkManager unitOfWorkManager)
        {
            _refListManger = formManger;
            _refListRepo = refListRepo;
            _refListItemRepo = refListItemRepo;
            _configItemRepository = configItemRepository;
            _moduleRepo = moduleRepo;
            _unitOfWorkManager = unitOfWorkManager;
        }

        public string ItemType => ReferenceList.ItemTypeName;

        /// inheritedDoc
        public async Task<ConfigurationItemBase> ImportItemAsync(DistributedConfigurableItemBase item, IConfigurationItemsImportContext context) 
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            if (!(item is DistributedReferenceList refListItem))
                throw new NotSupportedException($"{this.GetType().FullName} supports only items of type {nameof(DistributedReferenceList)}. Actual type is {item.GetType().FullName}");

            return await ImportRefListAsync(refListItem, context);
        }

        /// inheritedDoc
        public async Task<DistributedConfigurableItemBase> ReadFromJsonAsync(Stream jsonStream) 
        {
            using (var reader = new StreamReader(jsonStream))
            {
                var json = await reader.ReadToEndAsync();
                return JsonConvert.DeserializeObject<DistributedReferenceList>(json);
            }
        }

        /// inheritedDoc
        protected async Task<ConfigurationItemBase> ImportRefListAsync(DistributedReferenceList item, IConfigurationItemsImportContext context)
        {
            // check if form exists
            var existingList = await _refListRepo.FirstOrDefaultAsync(f => f.Configuration.Name == item.Name && (f.Configuration.Module == null && item.ModuleName == null || f.Configuration.Module.Name == item.ModuleName) && f.Configuration.IsLast);

            // use status specified in the context with fallback to imported value
            var statusToImport = context.ImportStatusAs ?? item.VersionStatus;
            if (existingList != null) 
            {
                switch (existingList.Configuration.VersionStatus) 
                {
                    case ConfigurationItemVersionStatus.Draft:
                    case ConfigurationItemVersionStatus.Ready: 
                    {
                        // cancel existing version
                        await _refListManger.CancelVersoinAsync(existingList);
                        break;
                    }
                }
                // mark existing live form as retired if we import new form as live
                if (statusToImport == ConfigurationItemVersionStatus.Live) 
                {
                    var liveForm = existingList.Configuration.VersionStatus == ConfigurationItemVersionStatus.Live
                        ? existingList
                        : await _refListRepo.FirstOrDefaultAsync(f => f.Configuration.Name == item.Name && (f.Configuration.Module == null && item.ModuleName == null || f.Configuration.Module.Name == item.ModuleName) && f.Configuration.VersionStatus == ConfigurationItemVersionStatus.Live);
                    if (liveForm != null)
                    {
                        await _refListManger.UpdateStatusAsync(liveForm, ConfigurationItemVersionStatus.Retired);
                        await _unitOfWorkManager.Current.SaveChangesAsync(); // save changes to guarantee sequence of update
                    }
                }

                // create new version
                var newListVersion = await _refListManger.CreateNewVersionAsync(existingList);
                MapToForm(item, newListVersion);

                // important: set status according to the context
                newListVersion.Configuration.VersionStatus = statusToImport;
                newListVersion.Configuration.CreatedByImport = context.ImportResult;
                newListVersion.Normalize();

                // todo: save external Id
                // how to handle origin?

                await _configItemRepository.UpdateAsync(newListVersion.Configuration);
                await _refListRepo.UpdateAsync(newListVersion);

                return newListVersion;
            } else 
            {
                var newList = new ReferenceList();
                MapToForm(item, newList);

                // fill audit?
                newList.Configuration.VersionNo = 1;
                newList.Configuration.Module = await GetModuleAsync(item.ModuleName, context);

                // important: set status according to the context
                newList.Configuration.VersionStatus = statusToImport;
                newList.Configuration.CreatedByImport = context.ImportResult;

                newList.Normalize();

                await _configItemRepository.InsertAsync(newList.Configuration);
                await _refListRepo.InsertAsync(newList);
                

                return newList;
            }
        }

        private async Task ImportListItems(ReferenceList refList, List<DistributedReferenceListItem> distributedItems)
        {
            await ImportListItemLevelAsync(refList, distributedItems, null);
        }
        private async Task ImportListItemLevelAsync(ReferenceList refList, List<DistributedReferenceListItem> items, ReferenceListItem parent)
        {
            foreach (var distributedItem in items)
            {
                var item = new ReferenceListItem();

                MapListItem(distributedItem, item);
                item.ReferenceList = refList;
                item.Parent = parent;

                await _refListItemRepo.InsertAsync(item);

                if (distributedItem.ChildItems != null && distributedItem.ChildItems.Any())
                    await ImportListItemLevelAsync(refList, distributedItem.ChildItems, item);
            }
        }


        private void MapListItem(DistributedReferenceListItem src, ReferenceListItem dst)
        {
            dst.Item = src.Item;
            dst.ItemValue = src.ItemValue;
            dst.Description = src.Description;
            dst.OrderIndex = src.OrderIndex;
            // todo: decide how to handle hard linked items
            //dst.HardLinkToApplication = src.HardLinkToApplication;
            dst.Color = src.Color;
            dst.Icon = src.Icon;
            dst.ShortAlias = src.ShortAlias;
        }

        private async Task<Module> GetModuleAsync(string name, IConfigurationItemsImportContext context) 
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var module = await _moduleRepo.FirstOrDefaultAsync(m => m.Name == name);
            if (module == null) 
            {
                if (context.CreateModules) 
                {
                    module = new Module { Name = name, IsEnabled = true };
                    await _moduleRepo.InsertAsync(module);
                } else
                    throw new NotSupportedException($"Module `{name}` is missing in the destination");
            }

            return module;
        }

        private void MapToForm(DistributedReferenceList item, ReferenceList form) 
        {
            form.Configuration.Name = item.Name;
            form.Configuration.Label = item.Label;
            form.Configuration.ItemType = item.ItemType;
            form.Configuration.Description = item.Description;
            form.Configuration.VersionStatus = item.VersionStatus;
            form.Configuration.Suppress = item.Suppress;

            /*
            form.Markup = item.Markup;
            form.ModelType = item.ModelType;
            */
            
            // todo: decide how to handle other properties
            /*
            form.Configuration.Origin
            form.Configuration.Module
            form.Configuration.BaseItem
            form.Configuration.VersionNo
            form.Configuration.ParentVersion
            form.Configuration.CreatedByImport
            form.Configuration.TenantId
            */
        }
    }
}
